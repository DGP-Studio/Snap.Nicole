using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sentry;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.Text.Json;
using Snap.Nicole.Core.Threading;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using Snap.Nicole.ViewModels.Agent;
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
        AgentConversationViewModel conversation = context.Conversation;

        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIChatStream, "Run streaming chat completion");
        span.SetTag(SentryTags.AIProvider, context.Options.ProviderType.ToString());
        span.SetTag(SentryTags.AIModel, context.Options.ModelId);

        try
        {
            if (ShouldDisplayInputMessage(context.InputMessage))
            {
                ObservableChatMessage inputMessage = ObservableChatMessage.Create(context.InputMessage, functionContentJsonOptions);
                await taskScheduler.Run(ObservableChatMessageCollection.Add, conversation.Messages, inputMessage, cancellationToken);
            }

            await Task.Yield();
            await foreach (AgentResponseUpdate update in context.Agent.RunStreamingAsync([context.InputMessage], context.Session, context.Options.AsAgentRunOptions(), cancellationToken))
            {
                ExtendedAgentResponseUpdate pendingUpdate = ExtendedAgentResponseUpdate.Create(update, functionContentJsonOptions);
                if (pendingUpdate.IsEmpty)
                {
                    // Fast path for updates that do not contain any content. This avoids unnecessary dispatching to the UI thread.
                    continue;
                }

                await taskScheduler.Run(ProcessPendingUpdate, context, pendingUpdate, cancellationToken);
            }

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
            await taskScheduler.Run(static (context, errorContent) =>
            {
                ObservableChatMessage responseMessage = context.EnsureTargetResponseMessage();
                responseMessage.Contents.AddOrUpdate(errorContent);
            }, context, errorContent, cancellationToken);

            return SpanStatus.InternalError;
        }
    }

    private static bool ShouldDisplayInputMessage(ChatMessage message)
    {
        // This method is lifted out for future extensibility
        // 1. Message with single ToolApprovalResponseContent is a special case where we don't want to display at all
        return message.Contents is not [ToolApprovalResponseContent];
    }

    private static void ProcessPendingUpdate(AgentRunStreamingContext context, ExtendedAgentResponseUpdate pendingUpdate)
    {
        if (pendingUpdate.ToolApprovalRequest is { } toolApprovalRequest)
        {
            ArgumentNullException.ThrowIfNull(context.TargetResponseMessage);
            toolApprovalRequest.TargetMessageId = context.TargetResponseMessage.Id;

            context.Conversation.ToolApprovalRequest = toolApprovalRequest;
        }

        if (pendingUpdate.ObservableContents.Count is 0)
        {
            return;
        }

        ObservableChatMessage responseMessage = context.EnsureTargetResponseMessage();
        foreach (ObservableAIContent observableContent in pendingUpdate.ObservableContents)
        {
            responseMessage.Contents.AddOrUpdate(observableContent);
        }
    }

    private static IList<AITool> CreateBuiltInTools()
    {
        return
        [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(BuiltInFunctions.GetCurrentTime)),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(BuiltInFunctions.ShowSummary))
        ];
    }
}