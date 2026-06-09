using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed partial class AgentConversationViewModel(IAgentConversationDeleteHandler conversationDeleteHandler, AgentConversationTurnController conversationTurnController)
    : ObservableObject, IDisposable
{
    private readonly IAgentConversationDeleteHandler conversationDeleteHandler = conversationDeleteHandler;
    private readonly AgentConversationTurnController conversationTurnController = conversationTurnController;

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
    public AgentConversationRuntimeState Runtime { get; } = new();

    [ObservableProperty]
    public partial ObservableChatMessageCollection Messages { get; set; } = [];

    private bool CanSendMessage { get => !disposed && generationCts is null && !IsBusy && ModelProviderProfile is not null && !string.IsNullOrWhiteSpace(InputText) && !string.IsNullOrWhiteSpace(ModelProfile?.ModelId); }

    [JsonIgnore]
    public bool CanStopGeneration { get => !disposed && generationCts is not null; }

    private bool CanDeleteConversation { get => !disposed && !IsBusy; }

    public StringResourceValue TitleDisplay { get => string.IsNullOrWhiteSpace(Title) ? SRName.UIXamlPagesAgentPageLabelNewConversation : Title; }

    public string CreatedAtDisplay { get => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }

    public string UpdatedAtDisplay { get => UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }

    partial void OnModelProviderProfileChanged(ModelProviderProfile? value)
    {
        Runtime.Reset();

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

        await conversationTurnController.SendMessageAsync(this, input, ConfigureObservableContent, cancellationToken);
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

        conversationDeleteHandler.DeleteConversation(this);
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
        Runtime.Reset();
    }

    private bool CanRespondToToolApproval(ObservableToolApprovalRequestContent? request)
    {
        return !disposed && conversationTurnController.CanRespondToToolApproval(this, request);
    }

    private Task RespondToToolApprovalAsync(ObservableToolApprovalRequestContent request, AIContent responseContent, CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }

        return conversationTurnController.RespondToToolApprovalAsync(this, request, responseContent, ConfigureObservableContent, NotifyToolApprovalCommandCanExecuteChanged, cancellationToken);
    }

    internal void NotifyToolApprovalCommandCanExecuteChanged()
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

    internal void NotifyTurnCommandCanExecuteChanged(bool notifyToolApprovalCommands)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
        StopGenerationCommand.NotifyCanExecuteChanged();
        if (notifyToolApprovalCommands)
        {
            NotifyToolApprovalCommandCanExecuteChanged();
        }
    }

    internal CancellationTokenSource? SetGenerationCancellationTokenSource(CancellationTokenSource? value)
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
