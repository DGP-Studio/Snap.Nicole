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

internal sealed class AgentService(IServiceProvider serviceProvider, AgentChatClientFactory chatClientFactory) : IAgentService
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private readonly AgentChatClientFactory chatClientFactory = chatClientFactory;
    private readonly JsonSerializerOptions functionContentJsonOptions = serviceProvider.GetRequiredKeyedService<JsonSerializerOptions>(JsonSerializerOptionsKey.AIFunctionContent);

    public ValueTask<HarnessAgent> CreateAgentAsync(ExtendedAgentOptions options, CancellationToken cancellationToken = default)
    {
        IChatClient chatClient = chatClientFactory.Create(options, serviceProvider);
        return ValueTask.FromResult(options.CreateHarnessAgent(chatClient, CreateBuiltInTools(), serviceProvider));
    }

    public ValueTask<AgentSession> CreateSessionAsync(HarnessAgent agent, CancellationToken cancellationToken = default)
    {
        return agent.CreateSessionAsync(cancellationToken);
    }

    public ValueTask<AgentSession> DeserializeSessionAsync(HarnessAgent agent, JsonElement serializedState, CancellationToken cancellationToken = default)
    {
        return agent.DeserializeSessionAsync(serializedState, cancellationToken: cancellationToken);
    }

    public ValueTask<JsonElement> SerializeSessionAsync(HarnessAgent agent, AgentSession session, CancellationToken cancellationToken = default)
    {
        return agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
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

            ObservableChatMessage? responseMessage = context.TargetResponseMessage;
            bool responseAdded = context.TargetResponseMessage is not null;

            await foreach (AgentResponseUpdate update in context.Agent.RunStreamingAsync([context.Message], context.Session, context.Options.AsAgentRunOptions(), cancellationToken))
            {
                List<ObservableAIContent> observableContents = [];
                foreach (AIContent content in update.Contents)
                {
                    if (ObservableAIContent.Create(content, functionContentJsonOptions) is { } observableContent)
                    {
                        context.ConfigureContent?.Invoke(observableContent);
                        observableContents.Add(observableContent);
                    }
                }

                // Fast path for updates that do not contain any content. This avoids unnecessary dispatching to the UI thread.
                if (observableContents.Count is 0)
                {
                    continue;
                }

                State state = new(context.Collection, context.Options, observableContents, responseMessage, responseAdded);

                await taskScheduler.Run(static (state) =>
                {
                    // TODOO: this should be possible to lift out of the UI thread dispatch,
                    // but currently if this is created on a background thread, the UI gets stuck and doesn't update at all.
                    state.ResponseMessage ??= ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, state.Options.ModelId);

                    if (!state.ResponseAdded)
                    {
                        state.Collection.Add(state.ResponseMessage);
                        state.ResponseAdded = true;
                    }

                    foreach (ObservableAIContent observableContent in state.ObservableContents)
                    {
                        state.ResponseMessage.Contents.AddOrUpdate(observableContent);
                    }
                }, state, cancellationToken);

                responseMessage = state.ResponseMessage;
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

            ObservableTextContent errorContent = ObservableTextContent.Create($"Error: {ex.Message}");
            if (context.TargetResponseMessage is not null)
            {
                await taskScheduler.Run(static (message, content) => message.Contents.AddOrUpdate(content), context.TargetResponseMessage, errorContent, cancellationToken);
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
        return [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(BuiltInFunctions.GetCurrentTime))];
    }

    private sealed class State(ObservableChatMessageCollection collection, ExtendedAgentOptions options, List<ObservableAIContent> observableContents, ObservableChatMessage? responseMessage, bool responseAdded)
    {
        public ObservableChatMessageCollection Collection { get; } = collection;

        public ExtendedAgentOptions Options { get; } = options;

        public List<ObservableAIContent> ObservableContents { get; } = observableContents;

        public ObservableChatMessage? ResponseMessage { get; set; } = responseMessage;

        public bool ResponseAdded { get; set; } = responseAdded;
    }
}
