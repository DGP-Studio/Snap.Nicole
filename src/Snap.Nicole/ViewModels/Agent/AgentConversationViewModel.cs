using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed partial class AgentConversationViewModel(IAgentConversationDeleteHandler conversationDeleteHandler, AgentConversationTurnController conversationTurnController, AgentConversationWorkspaceController conversationWorkspaceController)
    : ObservableObject, IDisposable
{
    private readonly IAgentConversationDeleteHandler conversationDeleteHandler = conversationDeleteHandler;
    private readonly AgentConversationTurnController conversationTurnController = conversationTurnController;
    private readonly AgentConversationWorkspaceController conversationWorkspaceController = conversationWorkspaceController;

    private bool disposed;

    public Guid Id { get; set; } = Guid.NewGuid();

    public AgentConversationRuntime Runtime { get; } = new();

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
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial ModelProviderProfile? ModelProviderProfile { get; set; }

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial ModelProfile? ModelProfile { get; set; }

    partial void OnModelProfileChanged(ModelProfile? value)
    {
        Runtime.Reset();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkspaceDisplayName), nameof(WorkspaceDisplayPath))]
    [NotifyCanExecuteChangedFor(nameof(SelectWorkspaceCommand), nameof(OpenWorkspaceCommand), nameof(ResetWorkspaceCommand))]
    public partial AgentConversationWorkspace Workspace { get; set; } = new();

    partial void OnWorkspaceChanged(AgentConversationWorkspace value)
    {
        Runtime.Reset();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand), nameof(DeleteConversationCommand), nameof(ApproveToolApprovalCommand), nameof(DenyToolApprovalCommand), nameof(SelectWorkspaceCommand), nameof(OpenWorkspaceCommand), nameof(ResetWorkspaceCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial AgentConversationStatisticsViewModel ConversationStatistics { get; private set; } = new();

    [ObservableProperty]
    public partial ObservableChatMessageCollection Messages { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand), nameof(ApproveToolApprovalCommand), nameof(DenyToolApprovalCommand), nameof(SelectWorkspaceCommand), nameof(OpenWorkspaceCommand), nameof(ResetWorkspaceCommand))]
    public partial ObservableToolApprovalRequestContent? ToolApprovalRequest { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStopGeneration))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand), nameof(StopGenerationCommand), nameof(DeleteConversationCommand), nameof(ApproveToolApprovalCommand), nameof(DenyToolApprovalCommand), nameof(SelectWorkspaceCommand), nameof(OpenWorkspaceCommand), nameof(ResetWorkspaceCommand))]
    private partial IAsyncRelayCommand? GenerationCommand { get; set; }

    private bool CanSendMessage { get => !disposed && GenerationCommand is null && !IsBusy && ToolApprovalRequest?.CanRespond is not true && ModelProviderProfile is not null && !string.IsNullOrWhiteSpace(InputText) && !string.IsNullOrWhiteSpace(ModelProfile?.ModelId); }

    public bool CanStopGeneration { get => !disposed && GenerationCommand is { IsCancellationRequested: false }; }

    private bool CanDeleteConversation { get => !disposed && GenerationCommand is null && !IsBusy; }

    private bool CanRespondToToolApproval { get => !disposed && GenerationCommand is null && AgentConversationTurnController.CanRespondToToolApproval(this); }

    private bool CanChangeWorkspace { get => !disposed && GenerationCommand is null && !IsBusy && ToolApprovalRequest?.CanRespond is not true; }

    public StringResourceValue TitleDisplay { get => string.IsNullOrWhiteSpace(Title) ? SRName.UIXamlPagesAgentPageLabelNewConversation : Title; }

    public StringResourceValue WorkspaceDisplayName { get => conversationWorkspaceController.GetWorkspaceDisplayName(this); }

    public string WorkspaceDisplayPath { get => conversationWorkspaceController.GetWorkspaceDisplayPath(this); }

    public string CreatedAtDisplay { get => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }

    public string UpdatedAtDisplay { get => UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, true))
        {
            return;
        }

        IAsyncRelayCommand? command = GenerationCommand;
        GenerationCommand = null;
        command?.Cancel();
        Runtime.Dispose();
    }

    public void UpdateConversationStatistics()
    {
        ConversationStatistics = AgentConversationStatisticsViewModel.Create(Messages);
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

    [RelayCommand(CanExecute = nameof(CanChangeWorkspace))]
    private async Task SelectWorkspaceAsync(CancellationToken cancellationToken)
    {
        if (!CanChangeWorkspace)
        {
            return;
        }

        await conversationWorkspaceController.SelectExternalWorkspaceAsync(this, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanChangeWorkspace))]
    private void OpenWorkspace()
    {
        if (!CanChangeWorkspace)
        {
            return;
        }

        conversationWorkspaceController.OpenWorkspace(this);
    }

    [RelayCommand(CanExecute = nameof(CanChangeWorkspace))]
    private void ResetWorkspace()
    {
        if (!CanChangeWorkspace)
        {
            return;
        }

        conversationWorkspaceController.ResetWorkspace(this);
    }

    [RelayCommand(CanExecute = nameof(CanStopGeneration))]
    private void StopGeneration()
    {
        if (disposed)
        {
            return;
        }

        if (GenerationCommand is not { } command)
        {
            return;
        }

        SentryDiagnostics.AddBreadcrumb("Stop chat generation", SentryBreadcrumbCategories.AIChat, SentryBreadcrumbTypes.UI);
        command.Cancel();
        OnPropertyChanged(nameof(CanStopGeneration));
        StopGenerationCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync(CancellationToken cancellationToken)
    {
        if (!CanSendMessage)
        {
            return;
        }

        string input = InputText.Trim();
        InputText = string.Empty;

        // There should not be a pending tool approval request when sending a normal message.
        Debug.Assert(ToolApprovalRequest is null);

        ChatMessage inputMessage = new(ChatRole.User, input)
        {
            CreatedAt = DateTimeOffset.Now,
            AuthorName = conversationTurnController.UserName,
        };

        await SendMessageCoreAsync(inputMessage, input, SendMessageCommand, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRespondToToolApproval))]
    private Task ApproveToolApprovalAsync(CancellationToken cancellationToken)
    {
        return SendToolApprovalAsync(true, null, ApproveToolApprovalCommand, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRespondToToolApproval))]
    private Task DenyToolApprovalAsync(CancellationToken cancellationToken)
    {
        return SendToolApprovalAsync(false, "Rejected by user", DenyToolApprovalCommand, cancellationToken);
    }

    private async Task SendToolApprovalAsync(bool approved, string? reason, IAsyncRelayCommand command, CancellationToken cancellationToken)
    {
        ObservableToolApprovalRequestContent? request = ToolApprovalRequest;
        if (request is null || disposed || !CanRespondToToolApproval || !ReferenceEquals(ToolApprovalRequest, request))
        {
            return;
        }

        ToolApprovalRequestContent runtimeContent = request.RawRepresentation ?? throw new InvalidOperationException("Tool approval request content is unavailable.");
        ToolApprovalResponseContent responseContent = reason is null ? runtimeContent.CreateResponse(approved) : runtimeContent.CreateResponse(approved, reason);
        ChatMessage responseMessage = new(ChatRole.User, [responseContent])
        {
            CreatedAt = DateTimeOffset.Now,
            AuthorName = conversationTurnController.UserName,
        };

        await SendMessageCoreAsync(responseMessage, null, command, cancellationToken);
    }

    private async Task SendMessageCoreAsync(ChatMessage message, string? titleInput, IAsyncRelayCommand command, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                GenerationCommand = command;
                await conversationTurnController.SendMessageAsync(this, message, titleInput, cancellationToken);
            }
            finally
            {
                GenerationCommand = null;
            }
        }
        catch (AgentConversationException ex)
        {
            conversationTurnController.AddExceptionMessages(this, ex);
        }
    }
}
