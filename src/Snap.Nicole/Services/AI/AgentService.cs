using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sentry;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.Text.Json;
using Snap.Nicole.Core.Threading;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentService(IServiceProvider serviceProvider) : IAgentService
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private readonly AgentChatClientFactory chatClientFactory = serviceProvider.GetRequiredService<AgentChatClientFactory>();
    private readonly JsonSerializerOptions functionContentJsonOptions = serviceProvider.GetRequiredKeyedService<JsonSerializerOptions>(JsonSerializerOptionsKey.AIFunctionContent);

    public ValueTask<HarnessAgent> CreateAgentAsync(ExtendedAgentOptions options, CancellationToken cancellationToken = default)
    {
        IChatClient chatClient = chatClientFactory.Create(options);
        return ValueTask.FromResult(options.CreateHarnessAgent(chatClient, CreateBuiltInTools(), serviceProvider));
    }

    public async ValueTask<SpanStatus> RunStreamingAsync(AgentRunStreamingContext context, TaskScheduler taskScheduler, CancellationToken cancellationToken)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIChatStream, "Run streaming chat completion");
        span.SetTag(SentryTags.AIProvider, context.Options.ProviderType.ToString());
        span.SetTag(SentryTags.AIModel, context.Options.ModelId);

        try
        {
            if (ShouldAddInputMessage(context.Message))
            {
                ObservableChatMessage inputMessage = ObservableChatMessage.Create(context.Message, functionContentJsonOptions);
                await taskScheduler.Run(ObservableChatMessageCollection.Add, context.Collection, inputMessage, cancellationToken);
            }

            ObservableChatMessage? targetResponseMessage = context.TargetResponseMessage;
            bool responseAdded = context.TargetResponseMessage is not null;

            await Task.Yield();
            await foreach (AgentResponseUpdate update in context.Agent.RunStreamingAsync([context.Message], context.Session, context.Options.AsAgentRunOptions(), cancellationToken))
            {
                List<ObservableAIContent> observableContents = [];
                ObservableToolApprovalRequestContent? toolApprovalRequest = null;
                foreach (AIContent content in update.Contents)
                {
                    if (ObservableAIContent.Create(content, functionContentJsonOptions) is { } observableContent)
                    {
                        context.ConfigureContent?.Invoke(observableContent);
                        if (observableContent is ObservableToolApprovalRequestContent request)
                        {
                            toolApprovalRequest = request;
                        }
                        else
                        {
                            observableContents.Add(observableContent);
                        }
                    }
                }

                // Fast path for updates that do not contain any content. This avoids unnecessary dispatching to the UI thread.
                if (observableContents.Count is 0 && toolApprovalRequest is null)
                {
                    continue;
                }

                State state = new()
                {
                    Collection = context.Collection,
                    Options = context.Options,
                    ObservableContents = observableContents,
                    TargetResponseMessage = targetResponseMessage,
                    ResponseAdded = responseAdded,
                    ToolApprovalRequest = toolApprovalRequest,
                    SetToolApprovalRequest = context.SetToolApprovalRequest,
                };

                await taskScheduler.Run(static (state) =>
                {
                    if (state.ToolApprovalRequest is not null)
                    {
                        if (state.TargetResponseMessage is not null || state.ObservableContents.Count is not 0)
                        {
                            state.ToolApprovalRequest.TargetMessageId = state.EnsureTargetResponseMessage().EnsureMessageId();
                        }

                        state.SetToolApprovalRequest?.Invoke(state.ToolApprovalRequest);
                    }

                    if (state.ObservableContents.Count is 0)
                    {
                        return;
                    }

                    // TODOO: this should be possible to lift out of the UI thread dispatch,
                    // but currently if this is created on a background thread, the UI gets stuck and doesn't update at all.
                    ObservableChatMessage responseMessage = state.EnsureTargetResponseMessage();

                    if (!state.ResponseAdded)
                    {
                        state.Collection.Add(responseMessage);
                        state.ResponseAdded = true;
                    }

                    foreach (ObservableAIContent observableContent in state.ObservableContents)
                    {
                        responseMessage.Contents.AddOrUpdate(observableContent);
                    }
                }, state, cancellationToken);

                targetResponseMessage = state.TargetResponseMessage;
                responseAdded = state.ResponseAdded;
            }

            span.SetData(SentryData.AIResponseAdded, responseAdded);
            return SpanStatus.Ok;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            span.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.AIChatStream);

            ObservableErrorContent errorContent = ObservableErrorContent.Create(ex.Message);
            if (context.TargetResponseMessage is not null)
            {
                await taskScheduler.Run(ObservableAIContentCollection.AddOrUpdate, context.TargetResponseMessage.Contents, errorContent, cancellationToken);
            }
            else
            {
                ObservableChatMessage errorMessage = ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, content: errorContent);
                await taskScheduler.Run(ObservableChatMessageCollection.Add, context.Collection, errorMessage, cancellationToken);
            }

            return SpanStatus.InternalError;
        }
    }

    private static bool ShouldAddInputMessage(ChatMessage message)
    {
        // This method is lifted out for future extensibility
        return message.Contents is not [ToolApprovalResponseContent];
    }

    private static IList<AITool> CreateBuiltInTools()
    {
        return
        [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(BuiltInFunctions.GetCurrentTime)),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(BuiltInFunctions.ShowSummary))
        ];
    }

    private sealed class State
    {
        public required ObservableChatMessageCollection Collection { get; init; }

        public required ExtendedAgentOptions Options { get; init; }

        public required List<ObservableAIContent> ObservableContents { get; init; }

        public ObservableChatMessage? TargetResponseMessage { get; set; }

        public bool ResponseAdded { get; set; }

        public ObservableToolApprovalRequestContent? ToolApprovalRequest { get; init; }

        public Action<ObservableToolApprovalRequestContent>? SetToolApprovalRequest { get; init; }

        public ObservableChatMessage EnsureTargetResponseMessage()
        {
            TargetResponseMessage ??= ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, Options.ModelId);
            return TargetResponseMessage;
        }
    }
}
