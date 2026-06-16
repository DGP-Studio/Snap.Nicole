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

    public static bool CanRespondToToolApproval(AgentConversationViewModel conversation, ObservableToolApprovalRequestContent? request)
    {
        if (conversation.IsBusy)
        {
            return false;
        }

        if (!ReferenceEquals(conversation.ToolApprovalRequest, request))
        {
            return false;
        }

        if (request?.CanRespond is not true)
        {
            return false;
        }

        return AgentConversationProfileController.CanCreateRequestOptions(conversation.ModelProviderProfile, conversation.ModelProfile);
    }

    public async Task SendMessageAsync(AgentConversationViewModel conversation, ChatMessage inputMessage, string? titleInput, CancellationToken cancellationToken)
    {
        AgentConversationTurnOperation operation = new()
        {
            InputMessage = inputMessage,
            TitleInput = titleInput,
        };

        await RunTurnAsync(conversation, operation, cancellationToken);
    }

    public void AddExceptionMessages(AgentConversationViewModel conversation, ChatMessage inputMessage, string? titleInput, Exception ex)
    {
        ObservableChatMessage observableInputMessage = ObservableChatMessage.Create(inputMessage, functionContentJsonOptions);
        conversation.Messages.Add(observableInputMessage);

        ObservableErrorContent errorContent = ObservableErrorContent.Create(ex.Message);
        ObservableChatMessage exceptionMessage = ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, content: errorContent);
        conversation.Messages.Add(exceptionMessage);

        conversation.UpdateTurnMetadata(titleInput);
        conversation.UpdateConversationStatistics();
    }

    public async Task RespondToToolApprovalAsync(AgentConversationViewModel conversation, ToolApprovalResponseContent responseContent, CancellationToken cancellationToken)
    {
        ObservableToolApprovalRequestContent? request = conversation.ToolApprovalRequest;
        if (!CanRespondToToolApproval(conversation, request) || request is null)
        {
            return;
        }

        AgentConversationTurnOperation operation = new()
        {
            InputMessage = new(ChatRole.User, [responseContent])
            {
                CreatedAt = DateTimeOffset.Now,
                AuthorName = UserName,
            },
            ToolApprovalState = new()
            {
                Request = request,
                ResponseContent = responseContent,
            },
        };

        await RunTurnAsync(conversation, operation, cancellationToken);
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

    private async Task RunTurnAsync(AgentConversationViewModel conversation, AgentConversationTurnOperation operation, CancellationToken cancellationToken)
    {
        if (profileController.CreateRequestOptions(conversation.ModelProviderProfile, conversation.ModelProfile) is not { } requestOptions)
        {
            // TODO: Consider throw there.
            return;
        }

        if (string.IsNullOrWhiteSpace(requestOptions.ApiKey))
        {
            conversation.Runtime.Reset();

            SentryDiagnostics.AddBreadcrumb("Chat blocked by missing API key", SentryBreadcrumbCategories.AIChat, SentryBreadcrumbTypes.UI);
            throw new AgentConversationException(SRName.UIXamlPagesAgentPageMessageConfigureApiKey);
        }

        HarnessAgent agent = await runtimeController.EnsureConversationAgentAsync(conversation.Runtime, requestOptions, cancellationToken);
        AgentSession session = await runtimeController.EnsureConversationSessionAsync(conversation.Runtime, agent, cancellationToken);
        AgentRunStreamingContext streamingContext = new()
        {
            Conversation = conversation,
            InputMessage = operation.InputMessage,
            Agent = agent,
            Session = session,
            Options = requestOptions,
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

            conversation.UpdateTurnMetadata(operation.TitleInput);

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
