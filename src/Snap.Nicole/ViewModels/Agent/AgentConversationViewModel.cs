using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sentry;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.Threading;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed partial class AgentConversationViewModel(IAgentService agentService, AgentConversationRuntimeCoordinator conversationRuntimeCoordinator, AgentConversationProfileCoordinator conversationProfileCoordinator, IAgentConversationOwner conversationOwner)
    : ObservableObject, IDisposable
{
    private readonly IAgentService agentService = agentService;
    private readonly AgentConversationRuntimeCoordinator conversationRuntimeCoordinator = conversationRuntimeCoordinator;
    private readonly AgentConversationProfileCoordinator conversationProfileCoordinator = conversationProfileCoordinator;
    private readonly IAgentConversationOwner conversationOwner = conversationOwner;
    private CancellationTokenSource? generationCts;
    private bool disposed;

    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleDisplay))]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreatedAtDisplay))]
    public partial DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedAtDisplay))]
    public partial DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    [ObservableProperty]
    [JsonIgnore]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial ModelProviderProfile? ModelProviderProfile { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial ModelProfile? ModelProfile { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    [JsonIgnore]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand), nameof(DeleteConversationCommand), nameof(ApproveToolApprovalCommand), nameof(DenyToolApprovalCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    public partial AgentConversationStatisticsViewModel ConversationStatistics { get; private set; } = new();

    [JsonIgnore]
    public HarnessAgent? Agent { get; set; }

    [JsonIgnore]
    public ExtendedAgentOptions? AgentOptions { get; set; }

    [JsonIgnore]
    public AgentSession? Session { get; set; }

    [JsonIgnore]
    public JsonElement? SerializedSessionState { get; set; }

    [ObservableProperty]
    public partial ObservableChatMessageCollection Messages { get; private set; } = [];

    private bool CanSendMessage { get => !disposed && generationCts is null && !IsBusy && ModelProviderProfile is not null && !string.IsNullOrWhiteSpace(InputText) && !string.IsNullOrWhiteSpace(ModelProfile?.ModelId); }

    [JsonIgnore]
    public bool CanStopGeneration { get => !disposed && generationCts is not null; }

    private bool CanDeleteConversation { get => !disposed && !IsBusy; }

    public StringResourceValue TitleDisplay { get => string.IsNullOrWhiteSpace(Title) ? SRName.UIXamlPagesAgentPageLabelNewConversation : Title; }

    public string CreatedAtDisplay { get => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }

    public string UpdatedAtDisplay { get => UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }

    public AgentConversation ToData()
    {
        return new()
        {
            Id = Id,
            Title = Title,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ModelProviderProfileId = ModelProviderProfile?.Id,
            ModelProfileId = ModelProfile?.Id,
            SerializedSessionState = SerializedSessionState?.Clone(),
            Messages = new(Messages),
        };
    }

    public static AgentConversationViewModel Create(AgentConversation data, IAgentService agentService, AgentConversationRuntimeCoordinator conversationRuntimeCoordinator, AgentConversationProfileCoordinator conversationProfileCoordinator, IAgentConversationOwner conversationOwner)
    {
        return new(agentService, conversationRuntimeCoordinator, conversationProfileCoordinator, conversationOwner)
        {
            Id = data.Id,
            Title = data.Title,
            CreatedAt = data.CreatedAt,
            UpdatedAt = data.UpdatedAt,
            SerializedSessionState = data.SerializedSessionState?.Clone(),
            Messages = new(data.Messages),
        };
    }

    partial void OnModelProviderProfileChanged(ModelProviderProfile? value)
    {
        AgentConversationRuntimeCoordinator.ResetConversationRuntime(this);

        if (value is null)
        {
            ModelProfile = null;
            return;
        }

        if (ModelProfile is null || !value.ModelProfiles.Contains(ModelProfile))
        {
            ModelProfile = value.ModelProfiles.CurrentItem;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync(CancellationToken cancellationToken)
    {
        if (!CanSendMessage)
        {
            return;
        }

        string input = InputText.Trim();
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        ExtendedAgentOptions? requestOptions = conversationProfileCoordinator.CreateRequestOptions(this);
        if (requestOptions is null)
        {
            return;
        }

        HarnessAgent? agent = null;
        AgentSession? session = null;
        if (!string.IsNullOrWhiteSpace(requestOptions.ApiKey))
        {
            agent = await conversationRuntimeCoordinator.EnsureConversationAgentAsync(this, requestOptions, cancellationToken);
            session = await conversationRuntimeCoordinator.EnsureConversationSessionAsync(this, agent, cancellationToken);
        }
        else
        {
            AgentConversationRuntimeCoordinator.ResetConversationRuntime(this);
        }

        ChatMessage userMessage = new(ChatRole.User, input)
        {
            CreatedAt = DateTimeOffset.Now,
            AuthorName = "You",
        };

        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIChatSend, "Send chat message");
        span.SetTag(SentryTags.AIProvider, requestOptions.ProviderType.ToString());
        span.SetTag(SentryTags.AIModel, requestOptions.ModelId);

        InputText = string.Empty;
        IsBusy = true;
        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetGenerationCancellationTokenSource(linkedCts);

        try
        {
            TaskScheduler taskScheduler = App.Current.Threading.TaskScheduler;
            SpanStatus result;
            if (string.IsNullOrWhiteSpace(requestOptions.ApiKey))
            {
                result = await AddMissingApiKeyMessagesAsync(input, userMessage.CreatedAt, userMessage.AuthorName, Messages, taskScheduler, linkedCts.Token);
            }
            else
            {
                if (agent is null || session is null)
                {
                    throw new InvalidOperationException("Agent runtime must be initialized before streaming chat.");
                }

                AgentRunStreamingContext streamingContext = new()
                {
                    Agent = agent,
                    Message = userMessage,
                    Collection = Messages,
                    Options = requestOptions,
                    Session = session,
                    TaskScheduler = taskScheduler,
                    ConfigureContent = ConfigureObservableContent,
                    CancellationToken = linkedCts.Token,
                };
                result = await agentService.RunStreamingAsync(streamingContext);
            }

            span.Finish(result);

            UpdatedAt = DateTimeOffset.Now;
            if (string.IsNullOrWhiteSpace(Title))
            {
                Title = CreateTitle(input);
            }

            if (agent is not null && session is not null)
            {
                await conversationRuntimeCoordinator.PersistConversationSessionAsync(this, agent, session, linkedCts.Token);
            }

            conversationOwner.SaveConversation(this);
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
            SetGenerationCancellationTokenSource(null);
            RebuildConversationStatistics();
            linkedCts.Dispose();
            IsBusy = false;
            SendMessageCommand.NotifyCanExecuteChanged();
            StopGenerationCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopGeneration))]
    private void StopGeneration()
    {
        if (disposed)
        {
            return;
        }

        CancellationTokenSource? cancellationTokenSource = SetGenerationCancellationTokenSource(null);
        if (cancellationTokenSource is null)
        {
            return;
        }

        SentryDiagnostics.AddBreadcrumb("Stop chat generation", SentryBreadcrumbCategories.AIChat, SentryBreadcrumbTypes.UI);
        cancellationTokenSource.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteConversation))]
    private void DeleteConversation()
    {
        if (!CanDeleteConversation)
        {
            return;
        }

        conversationOwner.DeleteConversation(this);
    }

    [RelayCommand(CanExecute = nameof(CanRespondToToolApproval))]
    private Task ApproveToolApprovalAsync(ObservableToolApprovalRequestContent request, CancellationToken cancellationToken)
    {
        ToolApprovalRequestContent runtimeContent = request.RawRepresentation ?? throw new InvalidOperationException("Tool approval request content is unavailable.");
        return RespondToToolApprovalAsync(request, runtimeContent.CreateResponse(true), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRespondToToolApproval))]
    private Task DenyToolApprovalAsync(ObservableToolApprovalRequestContent request, CancellationToken cancellationToken)
    {
        ToolApprovalRequestContent runtimeContent = request.RawRepresentation ?? throw new InvalidOperationException("Tool approval request content is unavailable.");
        return RespondToToolApprovalAsync(request, runtimeContent.CreateResponse(false, "Rejected by user"), cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, true))
        {
            return;
        }

        CancellationTokenSource? cancellationTokenSource = SetGenerationCancellationTokenSource(null);
        cancellationTokenSource?.Cancel();
        DeleteConversationCommand.NotifyCanExecuteChanged();
    }

    public void RebuildConversationStatistics()
    {
        ConversationStatistics = AgentConversationStatisticsViewModel.Create(Messages);
    }

    partial void OnMessagesChanged(ObservableChatMessageCollection value)
    {
        RebuildConversationStatistics();
    }

    partial void OnModelProfileChanged(ModelProfile? value)
    {
        AgentConversationRuntimeCoordinator.ResetConversationRuntime(this);
    }

    private bool CanRespondToToolApproval(ObservableToolApprovalRequestContent? request)
    {
        return !disposed && !IsBusy && request?.CanRespond is true && Agent is not null && Session is not null && AgentOptions is not null;
    }

    private async Task RespondToToolApprovalAsync(ObservableToolApprovalRequestContent request, AIContent responseContent, CancellationToken cancellationToken)
    {
        if (!CanRespondToToolApproval(request) || Agent is not { } agent || Session is not { } session || AgentOptions is not { } requestOptions)
        {
            return;
        }

        ToolApprovalResponseContent response = responseContent switch
        {
            ToolApprovalResponseContent toolApprovalResponseContent => toolApprovalResponseContent,
            _ => throw new InvalidOperationException("Unsupported tool approval response content."),
        };

        request.IsHandled = true;
        request.Approved = response.Approved;
        request.Reason = response.Reason;
        NotifyToolApprovalCommandCanExecuteChanged();
        ObservableChatMessage? targetResponseMessage = FindMessageContaining(request);

        ChatMessage responseMessage = new(ChatRole.User, [responseContent])
        {
            CreatedAt = DateTimeOffset.Now,
            AuthorName = "You",
        };

        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIChatSend, "Respond to tool approval");
        span.SetTag(SentryTags.AIProvider, requestOptions.ProviderType.ToString());
        span.SetTag(SentryTags.AIModel, requestOptions.ModelId);

        IsBusy = true;
        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetGenerationCancellationTokenSource(linkedCts);

        try
        {
            TaskScheduler taskScheduler = App.Current.Threading.TaskScheduler;
            AgentRunStreamingContext streamingContext = new()
            {
                Agent = agent,
                Message = responseMessage,
                Collection = Messages,
                Options = requestOptions,
                Session = session,
                TaskScheduler = taskScheduler,
                ConfigureContent = ConfigureObservableContent,
                CancellationToken = linkedCts.Token,
                AddInputMessage = false,
                TargetResponseMessage = targetResponseMessage,
            };
            SpanStatus result = await agentService.RunStreamingAsync(streamingContext);
            span.Finish(result);

            UpdatedAt = DateTimeOffset.Now;
            await conversationRuntimeCoordinator.PersistConversationSessionAsync(this, agent, session, linkedCts.Token);
            conversationOwner.SaveConversation(this);
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
            SetGenerationCancellationTokenSource(null);
            RebuildConversationStatistics();
            linkedCts.Dispose();
            IsBusy = false;
            SendMessageCommand.NotifyCanExecuteChanged();
            StopGenerationCommand.NotifyCanExecuteChanged();
            NotifyToolApprovalCommandCanExecuteChanged();
        }
    }

    private ObservableChatMessage? FindMessageContaining(ObservableAIContent content)
    {
        foreach (ObservableChatMessage message in Messages)
        {
            if (message.Contents.Contains(content))
            {
                return message;
            }
        }

        return null;
    }

    private void NotifyToolApprovalCommandCanExecuteChanged()
    {
        ApproveToolApprovalCommand.NotifyCanExecuteChanged();
        DenyToolApprovalCommand.NotifyCanExecuteChanged();
    }

    private void ConfigureObservableContent(ObservableAIContent content)
    {
        if (content is not ObservableToolApprovalRequestContent request)
        {
            return;
        }

        request.ApproveCommand = ApproveToolApprovalCommand;
        request.DenyCommand = DenyToolApprovalCommand;
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

    private CancellationTokenSource? SetGenerationCancellationTokenSource(CancellationTokenSource? value)
    {
        CancellationTokenSource? previous = Interlocked.Exchange(ref generationCts, value);
        if (ReferenceEquals(previous, value))
        {
            return previous;
        }

        OnPropertyChanged(nameof(CanStopGeneration));
        SendMessageCommand.NotifyCanExecuteChanged();
        StopGenerationCommand.NotifyCanExecuteChanged();
        NotifyToolApprovalCommandCanExecuteChanged();
        return previous;
    }
}
