using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sentry;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using Snap.Nicole.Services.Settings;
using System.Runtime.Serialization;
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

    public string UserName { get => AgentOptionsNormalizer.NormalizeUserName(settings.AgentOptions.UserName); }

    public bool CanRespondToToolApproval(AgentConversationViewModel conversation, ObservableToolApprovalRequestContent? request)
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

        return profileController.CreateRequestOptions(conversation.ModelProviderProfile, conversation.ModelProfile) is not null;
    }

    public async Task SendMessageAsync(AgentConversationViewModel conversation, string input, Action<ObservableAIContent> configureContent, CancellationToken cancellationToken)
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

        ChatMessage userMessage = new(ChatRole.User, input)
        {
            CreatedAt = DateTimeOffset.Now,
            AuthorName = UserName,
        };

        AgentConversationTurnOperation operation = new()
        {
            SpanDescription = "Send chat message",
            RequestOptions = requestOptions,
            StreamingContext = new()
            {
                Agent = agent,
                Message = userMessage,
                Collection = conversation.Messages,
                Options = requestOptions,
                Session = session,
                ConfigureContent = configureContent,
                SetToolApprovalRequest = conversation.SetToolApprovalRequest,
            },
            TitleInput = input,
        };

        await RunConversationOperationAsync(conversation, operation, cancellationToken);
    }

    public async Task RespondToToolApprovalAsync(AgentConversationViewModel conversation, ObservableToolApprovalRequestContent request, AIContent responseContent, Action<ObservableAIContent> configureContent, Action notifyToolApprovalCommands, CancellationToken cancellationToken)
    {
        if (!CanRespondToToolApproval(conversation, request))
        {
            return;
        }

        if (profileController.CreateRequestOptions(conversation.ModelProviderProfile, conversation.ModelProfile) is not { } requestOptions)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(requestOptions.ApiKey))
        {
            conversation.Runtime.Reset();

            SentryDiagnostics.AddBreadcrumb("Tool approval response blocked by missing API key", SentryBreadcrumbCategories.AIChat, SentryBreadcrumbTypes.UI);
            throw new AgentConversationException(SRName.UIXamlPagesAgentPageMessageConfigureApiKey);
        }

        HarnessAgent agent = await runtimeController.EnsureConversationAgentAsync(conversation.Runtime, requestOptions, cancellationToken);
        AgentSession session = await runtimeController.EnsureConversationSessionAsync(conversation.Runtime, agent, cancellationToken);

        ToolApprovalResponseContent response = responseContent switch
        {
            ToolApprovalResponseContent toolApprovalResponseContent => toolApprovalResponseContent,
            _ => throw new InvalidOperationException("Unsupported tool approval response content."),
        };

        request.IsHandled = true;
        request.Approved = response.Approved;
        request.Reason = response.Reason;
        notifyToolApprovalCommands();
        ObservableChatMessage? targetResponseMessage = FindMessageById(conversation.Messages, request.TargetMessageId);
        conversation.ToolApprovalRequest = null;

        ChatMessage responseMessage = new(ChatRole.User, [responseContent])
        {
            CreatedAt = DateTimeOffset.Now,
            AuthorName = UserName,
        };

        AgentConversationTurnOperation operation = new()
        {
            SpanDescription = "Respond to tool approval",
            RequestOptions = requestOptions,
            StreamingContext = new()
            {
                Agent = agent,
                Message = responseMessage,
                Collection = conversation.Messages,
                Options = requestOptions,
                Session = session,
                ConfigureContent = configureContent,
                SetToolApprovalRequest = conversation.SetToolApprovalRequest,
                TargetResponseMessage = targetResponseMessage,
            },
            NotifyToolApprovalCommands = true,
        };

        await RunConversationOperationAsync(conversation, operation, cancellationToken);
    }

    private static ObservableChatMessage? FindMessageById(ObservableChatMessageCollection messages, string? messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        foreach (ObservableChatMessage message in messages)
        {
            if (string.Equals(message.MessageId, messageId, StringComparison.Ordinal))
            {
                return message;
            }
        }

        return null;
    }

    private async Task RunConversationOperationAsync(AgentConversationViewModel conversation, AgentConversationTurnOperation operation, CancellationToken cancellationToken)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIChatSend, operation.SpanDescription);
        span.SetTag(SentryTags.AIProvider, operation.RequestOptions.ProviderType.ToString());
        span.SetTag(SentryTags.AIModel, operation.RequestOptions.ModelId);

        conversation.IsBusy = true;

        try
        {
            TaskScheduler taskScheduler = App.Current.Threading.TaskScheduler;
            span.Finish(await agentService.RunStreamingAsync(operation.StreamingContext, taskScheduler, cancellationToken));

            conversation.UpdateTurnMetadata(operation.TitleInput);

            await runtimeController.PersistConversationSessionAsync(conversation.Runtime, operation.StreamingContext.Agent, operation.StreamingContext.Session, cancellationToken);
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
            conversation.RebuildConversationStatistics();
            conversation.IsBusy = false;
            conversation.CommandNotifier.NotifyTurnChanged(operation.NotifyToolApprovalCommands);
        }
    }

    private sealed class AgentConversationTurnOperation
    {
        public required string SpanDescription { get; init; }

        public required ExtendedAgentOptions RequestOptions { get; init; }

        public required AgentRunStreamingContext StreamingContext { get; init; }

        public string? TitleInput { get; init; }

        public bool NotifyToolApprovalCommands { get; init; }
    }
}
