using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sentry;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.Threading;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationTurnController(IServiceProvider serviceProvider)
{
    private readonly IAgentService agentService = serviceProvider.GetRequiredService<IAgentService>();
    private readonly AgentConversationProfileController profileController = serviceProvider.GetRequiredService<AgentConversationProfileController>();
    private readonly AgentConversationRuntimeController runtimeController = serviceProvider.GetRequiredService<AgentConversationRuntimeController>();
    private readonly AgentConversationPersistenceController persistenceController = serviceProvider.GetRequiredService<AgentConversationPersistenceController>();

    public async Task SendMessageAsync(AgentConversationViewModel conversation, string input, Action<ObservableAIContent> configureContent, CancellationToken cancellationToken)
    {
        ExtendedAgentOptions? requestOptions = profileController.CreateRequestOptions(conversation.ModelProviderProfile, conversation.ModelProfile);
        if (requestOptions is null)
        {
            return;
        }

        HarnessAgent? agent = null;
        AgentSession? session = null;
        if (!string.IsNullOrWhiteSpace(requestOptions.ApiKey))
        {
            agent = await runtimeController.EnsureConversationAgentAsync(conversation.Runtime, requestOptions, cancellationToken);
            session = await runtimeController.EnsureConversationSessionAsync(conversation.Runtime, agent, cancellationToken);
        }
        else
        {
            conversation.Runtime.Reset();
        }

        // TODO: Add a global authorName configuration for the user.
        ChatMessage userMessage = new(ChatRole.User, input)
        {
            CreatedAt = DateTimeOffset.Now,
            AuthorName = "You",
        };

        conversation.InputText = string.Empty;

        AgentRunStreamingContext? streamingContext = null;
        AgentConversationMissingApiKeyTurn? missingApiKeyTurn = null;
        if (string.IsNullOrWhiteSpace(requestOptions.ApiKey))
        {
            missingApiKeyTurn = new()
            {
                Input = input,
                CreatedAt = userMessage.CreatedAt,
                AuthorName = userMessage.AuthorName,
                Collection = conversation.Messages,
            };
        }
        else
        {
            if (agent is null || session is null)
            {
                throw new InvalidOperationException("Agent runtime must be initialized before streaming chat.");
            }

            streamingContext = new()
            {
                Agent = agent,
                Message = userMessage,
                Collection = conversation.Messages,
                Options = requestOptions,
                Session = session,
                ConfigureContent = configureContent,
            };
        }

        AgentConversationTurnOperation operation = new()
        {
            SpanDescription = "Send chat message",
            RequestOptions = requestOptions,
            StreamingContext = streamingContext,
            MissingApiKeyTurn = missingApiKeyTurn,
            TitleInput = input,
        };

        await RunConversationOperationAsync(conversation, operation, cancellationToken);
    }

    public bool CanRespondToToolApproval(AgentConversationViewModel conversation, ObservableToolApprovalRequestContent? request)
    {
        return !conversation.IsBusy && request?.CanRespond is true && conversation.Runtime.Agent is not null && conversation.Runtime.Session is not null && conversation.Runtime.AgentOptions is not null;
    }

    public Task RespondToToolApprovalAsync(AgentConversationViewModel conversation, ObservableToolApprovalRequestContent request, AIContent responseContent, Action<ObservableAIContent> configureContent, Action notifyToolApprovalCommands, CancellationToken cancellationToken)
    {
        if (!CanRespondToToolApproval(conversation, request) || conversation.Runtime.Agent is not { } agent || conversation.Runtime.Session is not { } session || conversation.Runtime.AgentOptions is not { } requestOptions)
        {
            return Task.CompletedTask;
        }

        ToolApprovalResponseContent response = responseContent switch
        {
            ToolApprovalResponseContent toolApprovalResponseContent => toolApprovalResponseContent,
            _ => throw new InvalidOperationException("Unsupported tool approval response content."),
        };

        request.IsHandled = true;
        request.Approved = response.Approved;
        request.Reason = response.Reason;
        notifyToolApprovalCommands();
        ObservableChatMessage? targetResponseMessage = FindMessageContaining(conversation, request);

        ChatMessage responseMessage = new(ChatRole.User, [responseContent])
        {
            CreatedAt = DateTimeOffset.Now,
            AuthorName = "You",
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
                TargetResponseMessage = targetResponseMessage,
            },
            NotifyToolApprovalCommands = true,
        };
        return RunConversationOperationAsync(conversation, operation, cancellationToken);
    }

    private ValueTask<SpanStatus> ExecuteTurnAsync(AgentConversationTurnOperation operation, TaskScheduler taskScheduler, CancellationToken cancellationToken)
    {
        if (operation.MissingApiKeyTurn is { } missingApiKeyTurn)
        {
            return AddMissingApiKeyMessagesAsync(missingApiKeyTurn.Input, missingApiKeyTurn.CreatedAt, missingApiKeyTurn.AuthorName, missingApiKeyTurn.Collection, taskScheduler, cancellationToken);
        }

        if (operation.StreamingContext is { } streamingContext)
        {
            return agentService.RunStreamingAsync(streamingContext, taskScheduler, cancellationToken);
        }

        throw new InvalidOperationException("Conversation turn operation must define execution state.");
    }

    private async Task RunConversationOperationAsync(AgentConversationViewModel conversation, AgentConversationTurnOperation operation, CancellationToken cancellationToken)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIChatSend, operation.SpanDescription);
        span.SetTag(SentryTags.AIProvider, operation.RequestOptions.ProviderType.ToString());
        span.SetTag(SentryTags.AIModel, operation.RequestOptions.ModelId);

        conversation.IsBusy = true;
        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        conversation.SetGenerationCancellationTokenSource(linkedCts);

        try
        {
            TaskScheduler taskScheduler = App.Current.Threading.TaskScheduler;
            SpanStatus result = await ExecuteTurnAsync(operation, taskScheduler, linkedCts.Token);
            span.Finish(result);

            conversation.UpdatedAt = DateTimeOffset.Now;
            if (operation.TitleInput is string titleInput && string.IsNullOrWhiteSpace(conversation.Title))
            {
                conversation.Title = CreateTitle(titleInput);
            }

            if (operation.StreamingContext is { } streamingContext)
            {
                await PersistRuntimeSessionAsync(conversation, streamingContext, linkedCts.Token);
            }

            persistenceController.SaveConversation(conversation);
        }
        catch (OperationCanceledException)
        {
            span.Finish(SpanStatus.Cancelled);
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.AIChatSend);
            throw;
        }
        finally
        {
            conversation.SetGenerationCancellationTokenSource(null);
            conversation.RebuildConversationStatistics();
            linkedCts.Dispose();
            conversation.IsBusy = false;
            conversation.NotifyTurnCommandCanExecuteChanged(operation.NotifyToolApprovalCommands);
        }
    }

    private ValueTask PersistRuntimeSessionAsync(AgentConversationViewModel conversation, AgentRunStreamingContext streamingContext, CancellationToken cancellationToken)
    {
        return runtimeController.PersistConversationSessionAsync(conversation.Runtime, streamingContext.Agent, streamingContext.Session, cancellationToken);
    }

    private static ObservableChatMessage? FindMessageContaining(AgentConversationViewModel conversation, ObservableAIContent content)
    {
        foreach (ObservableChatMessage message in conversation.Messages)
        {
            if (message.Contents.Contains(content))
            {
                return message;
            }
        }

        return null;
    }

    private static async ValueTask<SpanStatus> AddMissingApiKeyMessagesAsync(string input, DateTimeOffset? createdAt, string? authorName, ObservableChatMessageCollection collection, TaskScheduler taskScheduler, CancellationToken cancellationToken)
    {
        SentryDiagnostics.AddBreadcrumb("Chat blocked by missing API key", SentryBreadcrumbCategories.AIChat, SentryBreadcrumbTypes.UI);

        ObservableChatMessage inputMessage = ObservableChatMessage.Create(ChatRole.User, createdAt, authorName, ObservableTextContent.Create(input));
        await taskScheduler.Run(ObservableChatMessageCollection.Add, collection, inputMessage, cancellationToken);

        // ObservableTextContent stores a text snapshot, so this message cannot hot-switch after it is added.
        ObservableChatMessage configurationMessage = ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, content: ObservableTextContent.Create(StringResourceProxy.Default[SRName.UIXamlPagesAgentPageMessageConfigureApiKey]));
        await taskScheduler.Run(ObservableChatMessageCollection.Add, collection, configurationMessage, cancellationToken);
        return SpanStatus.FailedPrecondition;
    }

    private static string CreateTitle(string input)
    {
        string title = input.ReplaceLineEndings(" ").Trim();
        const int maxLength = 40;
        if (title.Length <= maxLength)
        {
            return title;
        }

        return title[..maxLength] + "...";
    }

    private sealed class AgentConversationTurnOperation
    {
        public required string SpanDescription { get; init; }

        public required ExtendedAgentOptions RequestOptions { get; init; }

        public AgentRunStreamingContext? StreamingContext { get; init; }

        public AgentConversationMissingApiKeyTurn? MissingApiKeyTurn { get; init; }

        public string? TitleInput { get; init; }

        public bool NotifyToolApprovalCommands { get; init; }
    }

    private sealed class AgentConversationMissingApiKeyTurn
    {
        public required string Input { get; init; }

        public DateTimeOffset? CreatedAt { get; init; }

        public string? AuthorName { get; init; }

        public required ObservableChatMessageCollection Collection { get; init; }
    }
}
