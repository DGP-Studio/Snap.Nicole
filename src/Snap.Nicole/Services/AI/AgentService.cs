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

    public ValueTask<AgentCreationResult> CreateAgentAsync(ExtendedAgentOptions options, AgentWorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        IChatClient chatClient = chatClientFactory.Create(options);
        return ValueTask.FromResult(options.CreateHarnessAgent(chatClient, CreateBuiltInTools(), serviceProvider, workspace));
    }

    public async ValueTask<SpanStatus> RunStreamingAsync(AgentRunStreamingContext context, TaskScheduler taskScheduler, CancellationToken cancellationToken)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIChatStream, "Run streaming chat completion");
        span.SetTag(SentryTags.AIProvider, context.Options.ProviderType.ToString());
        span.SetTag(SentryTags.AIModel, context.Options.ModelId);

        try
        {
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

    private static void ProcessPendingUpdate(AgentRunStreamingContext context, ExtendedAgentResponseUpdate pendingUpdate)
    {
        if (pendingUpdate.ToolApprovalRequest is { } toolApprovalRequest)
        {
            // Certain updates may only contains a single tool approval request without any content.
            // For example, when the reasoning_effort is set to none.
            toolApprovalRequest.TargetMessageId = context.EnsureTargetResponseMessage().Id;
            ApplyToolApprovalMetadata(context, toolApprovalRequest);
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

    private static void ApplyToolApprovalMetadata(AgentRunStreamingContext context, ObservableToolApprovalRequestContent toolApprovalRequest)
    {
        if (toolApprovalRequest.ToolCall is not ObservableFunctionCallContent functionCall)
        {
            return;
        }

        if (!IsWorkspaceScopedApprovalTool(functionCall.Name))
        {
            return;
        }

        string workingDirectory = string.Equals(functionCall.Name, "run_shell", StringComparison.Ordinal) ? context.Workspace.WorkingDirectory : string.Join(Environment.NewLine, context.Workspace.WorkingDirectories);
        toolApprovalRequest.WorkingDirectory = workingDirectory;
        Dictionary<string, string> data = new()
        {
            [SentryData.Item] = functionCall.Name,
            [SentryData.AgentWorkspaceDirectory] = string.Join("; ", context.Workspace.WorkingDirectories),
        };
        SentryDiagnostics.AddBreadcrumb("Workspace tool approval requested", SentryBreadcrumbCategories.AIChat, SentryBreadcrumbTypes.Navigation, data);
    }

    private static bool IsWorkspaceScopedApprovalTool(string name)
    {
        return string.Equals(name, "run_shell", StringComparison.Ordinal)
            || string.Equals(name, "FileAccess_SaveFile", StringComparison.Ordinal)
            || string.Equals(name, "FileAccess_DeleteFile", StringComparison.Ordinal);
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
