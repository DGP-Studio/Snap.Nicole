using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sentry;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.Text.Json;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using Snap.Nicole.Services.Settings;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationTurnController(IServiceProvider serviceProvider)
{
    private readonly IAgentService agentService = serviceProvider.GetRequiredService<IAgentService>();
    private readonly AppSettings settings = serviceProvider.GetRequiredService<ISettingsProvider<AppSettings>>().CurrentValue;
    private readonly AgentConversationProfileController profileController = serviceProvider.GetRequiredService<AgentConversationProfileController>();
    private readonly AgentConversationRuntimeController runtimeController = serviceProvider.GetRequiredService<AgentConversationRuntimeController>();
    private readonly AgentConversationPersistenceController persistenceController = serviceProvider.GetRequiredService<AgentConversationPersistenceController>();
    private readonly JsonSerializerOptions functionContentJsonOptions = serviceProvider.GetRequiredKeyedService<JsonSerializerOptions>(JsonSerializerOptionsKey.AIFunctionContent);

    public string UserName { get => AgentOptionsNormalizer.NormalizeUserName(settings.AgentOptions.UserName); }

    public static bool CanRespondToToolApproval(AgentConversationViewModel conversation)
    {
        if (conversation.IsBusy)
        {
            return false;
        }

        if (conversation.ToolApprovalRequest?.CanRespond is not true)
        {
            return false;
        }

        return AgentConversationProfileController.CanCreateRequestOptions(conversation.ModelProviderProfile, conversation.ModelProfile);
    }

    public async Task SendMessageAsync(AgentConversationViewModel conversation, ChatMessage inputMessage, string? titleInput, CancellationToken cancellationToken)
    {
        ToolApprovalResponseContent? toolApprovalResponseContent = GetToolApprovalResponseContent(inputMessage);
        AgentConversationToolApprovalTurnState? toolApprovalState = null;
        if (toolApprovalResponseContent is not null)
        {
            ObservableToolApprovalRequestContent? request = conversation.ToolApprovalRequest;
            if (request is null || !CanRespondToToolApproval(conversation) || !ReferenceEquals(conversation.ToolApprovalRequest, request))
            {
                return;
            }

            toolApprovalState = new()
            {
                Request = request,
                ResponseContent = toolApprovalResponseContent,
            };
        }

        AgentConversationTurnOperation operation = new()
        {
            InputMessage = inputMessage,
            TitleInput = toolApprovalResponseContent is null ? titleInput : null,
            ToolApprovalState = toolApprovalState,
        };

        await RunTurnAsync(conversation, operation, cancellationToken);
    }

    public void AddExceptionMessages(AgentConversationViewModel conversation, Exception ex)
    {
        ObservableErrorContent errorContent = ObservableErrorContent.Create(ex.Message);
        ObservableChatMessage exceptionMessage = ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, content: errorContent);
        conversation.Messages.Add(exceptionMessage);

        conversation.UpdateConversationStatistics();
    }

    private static ObservableChatMessage? FindMessageById(ObservableChatMessageCollection messages, Guid messageId)
    {
        foreach (ObservableChatMessage message in messages)
        {
            if (message.Id.Equals(messageId))
            {
                return message;
            }
        }

        return null;
    }

    private static bool ShouldDisplayInputMessage(ChatMessage message)
    {
        return GetToolApprovalResponseContent(message) is null;
    }

    private static ToolApprovalResponseContent? GetToolApprovalResponseContent(ChatMessage message)
    {
        if (message.Contents is [ToolApprovalResponseContent responseContent])
        {
            return responseContent;
        }

        return null;
    }

    private static string CreateTitle(string input)
    {
        // TODO: Generate title based on first conversation content
        string title = input.ReplaceLineEndings(" ").Trim();
        const int maxLength = 40;
        if (title.Length <= maxLength)
        {
            return title;
        }

        return title[..maxLength] + "...";
    }

    private static ObservableChatMessage? ApplyToolApprovalState(AgentConversationViewModel conversation, AgentConversationToolApprovalTurnState? state)
    {
        if (state is null)
        {
            return null;
        }

        ObservableToolApprovalRequestContent request = state.Request;
        ToolApprovalResponseContent responseContent = state.ResponseContent;

        request.IsHandled = true;
        request.Approved = responseContent.Approved;
        request.Reason = responseContent.Reason;

        ObservableChatMessage? targetResponseMessage = FindMessageById(conversation.Messages, request.TargetMessageId);
        conversation.ToolApprovalRequest = null;
        return targetResponseMessage;
    }

    private void AddInputMessage(AgentConversationViewModel conversation, ChatMessage inputMessage)
    {
        if (!ShouldDisplayInputMessage(inputMessage))
        {
            return;
        }

        ObservableChatMessage observableInputMessage = ObservableChatMessage.Create(inputMessage, functionContentJsonOptions);
        conversation.Messages.Add(observableInputMessage);
    }

    private async Task RunTurnAsync(AgentConversationViewModel conversation, AgentConversationTurnOperation operation, CancellationToken cancellationToken)
    {
        if (profileController.CreateRequestOptions(conversation.ModelProviderProfile, conversation.ModelProfile) is not { } requestOptions)
        {
            // TODO: Consider throw there.
            return;
        }

        AddInputMessage(conversation, operation.InputMessage);

        if (string.IsNullOrWhiteSpace(requestOptions.ApiKey))
        {
            conversation.Runtime.Reset();

            SentryDiagnostics.AddBreadcrumb("Chat blocked by missing API key", SentryBreadcrumbCategories.AIChat, SentryBreadcrumbTypes.UI);
            throw new AgentConversationException(SRName.UIXamlPagesAgentPageMessageConfigureApiKey);
        }

        HarnessAgent agent = await runtimeController.EnsureConversationAgentAsync(conversation, requestOptions, cancellationToken);
        AgentSession session = await runtimeController.EnsureConversationSessionAsync(conversation.Runtime, agent, cancellationToken);
        AgentWorkspaceSnapshot workspace = conversation.Runtime.Workspace ?? throw new InvalidOperationException("Agent workspace is unavailable.");
        AgentRunStreamingContext streamingContext = new()
        {
            Conversation = conversation,
            InputMessage = operation.InputMessage,
            Agent = agent,
            Session = session,
            Options = requestOptions,
            Workspace = workspace,
            TargetResponseMessage = ApplyToolApprovalState(conversation, operation.ToolApprovalState),
        };

        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIChatSend, "Run chat turn");
        span.SetTag(SentryTags.AIProvider, requestOptions.ProviderType.ToString());
        span.SetTag(SentryTags.AIModel, requestOptions.ModelId);

        conversation.IsBusy = true;

        try
        {
            TaskScheduler taskScheduler = App.Current.Threading.TaskScheduler;
            span.Finish(await agentService.RunStreamingAsync(streamingContext, taskScheduler, cancellationToken));

            conversation.UpdatedAt = DateTimeOffset.Now;
            if (operation.TitleInput is not null && string.IsNullOrWhiteSpace(conversation.Title))
            {
                conversation.Title = CreateTitle(operation.TitleInput);
            }

            await runtimeController.SerializeConversationSessionAsync(conversation.Runtime, streamingContext.Agent, streamingContext.Session, cancellationToken);
            persistenceController.SaveConversation(conversation);
        }
        catch (OperationCanceledException)
        {
            span.Finish(SpanStatus.Cancelled);
        }
        catch (AgentConversationException)
        {
            span.Finish(SpanStatus.FailedPrecondition);
            throw;
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.AIChatSend);
            throw;
        }
        finally
        {
            conversation.UpdateConversationStatistics();
            conversation.IsBusy = false;
        }
    }

    private sealed class AgentConversationTurnOperation
    {
        public required ChatMessage InputMessage { get; init; }

        public string? TitleInput { get; init; }

        public AgentConversationToolApprovalTurnState? ToolApprovalState { get; init; }
    }

    private sealed class AgentConversationToolApprovalTurnState
    {
        public required ObservableToolApprovalRequestContent Request { get; init; }

        public required ToolApprovalResponseContent ResponseContent { get; init; }
    }
}
