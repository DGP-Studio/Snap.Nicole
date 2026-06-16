using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed partial class AgentConversationViewModel
    : ObservableObject, IDisposable
{
    private readonly IAgentConversationDeleteHandler conversationDeleteHandler;
    private readonly AgentConversationTurnController conversationTurnController;
    private readonly PropertyScopeFactory<AgentConversationViewModel, IAsyncRelayCommand> generationCommandScopeFactory;

    private bool disposed;

    public AgentConversationViewModel(IAgentConversationDeleteHandler conversationDeleteHandler, AgentConversationTurnController conversationTurnController)
    {
        this.conversationDeleteHandler = conversationDeleteHandler;
        this.conversationTurnController = conversationTurnController;

        generationCommandScopeFactory = new(this, static (self, value) => self.GenerationCommand = value);
    }

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
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand), nameof(DeleteConversationCommand), nameof(ApproveToolApprovalCommand), nameof(DenyToolApprovalCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial AgentConversationStatisticsViewModel ConversationStatistics { get; private set; } = new();

    public AgentConversationRuntime Runtime { get; } = new();

    [ObservableProperty]
    public partial ObservableChatMessageCollection Messages { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand), nameof(ApproveToolApprovalCommand), nameof(DenyToolApprovalCommand))]
    public partial ObservableToolApprovalRequestContent? ToolApprovalRequest { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStopGeneration))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand), nameof(StopGenerationCommand), nameof(DeleteConversationCommand), nameof(ApproveToolApprovalCommand), nameof(DenyToolApprovalCommand))]
    private partial IAsyncRelayCommand? GenerationCommand { get; set; }

    private bool CanSendMessage { get => !disposed && GenerationCommand is null && !IsBusy && ToolApprovalRequest?.CanRespond is not true && ModelProviderProfile is not null && !string.IsNullOrWhiteSpace(InputText) && !string.IsNullOrWhiteSpace(ModelProfile?.ModelId); }

    public bool CanStopGeneration { get => !disposed && GenerationCommand is { IsCancellationRequested: false }; }

    private bool CanDeleteConversation { get => !disposed && GenerationCommand is null && !IsBusy; }

    public StringResourceValue TitleDisplay { get => string.IsNullOrWhiteSpace(Title) ? SRName.UIXamlPagesAgentPageLabelNewConversation : Title; }

    public string CreatedAtDisplay { get => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }

    public string UpdatedAtDisplay { get => UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }

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

        ChatMessage inputMessage = new(ChatRole.User, input)
        {
            CreatedAt = DateTimeOffset.Now,
            AuthorName = conversationTurnController.UserName,
        };

        InputText = string.Empty;
        ToolApprovalRequest = null;

        try
        {
            using (generationCommandScopeFactory.Create(SendMessageCommand, null))
            {
                await conversationTurnController.SendMessageAsync(this, inputMessage, input, cancellationToken);
            }
        }
        catch (AgentConversationException ex)
        {
            conversationTurnController.AddExceptionMessages(this, inputMessage, input, ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopGeneration))]
    private void StopGeneration()
    {
        if (disposed)
        {
            return;
        }

        IAsyncRelayCommand? command = GenerationCommand;
        if (command is null)
        {
            return;
        }

        SentryDiagnostics.AddBreadcrumb("Stop chat generation", SentryBreadcrumbCategories.AIChat, SentryBreadcrumbTypes.UI);
        command.Cancel();
        OnPropertyChanged(nameof(CanStopGeneration));
        StopGenerationCommand.NotifyCanExecuteChanged();
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
    private async Task ApproveToolApprovalAsync(ObservableToolApprovalRequestContent request, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(ToolApprovalRequest, request))
        {
            return;
        }

        ToolApprovalRequestContent runtimeContent = request.RawRepresentation ?? throw new InvalidOperationException("Tool approval request content is unavailable.");

        using (generationCommandScopeFactory.Create(ApproveToolApprovalCommand, null))
        {
            await RespondToToolApprovalAsync(request, runtimeContent.CreateResponse(true), cancellationToken);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRespondToToolApproval))]
    private async Task DenyToolApprovalAsync(ObservableToolApprovalRequestContent request, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(ToolApprovalRequest, request))
        {
            return;
        }

        ToolApprovalRequestContent runtimeContent = request.RawRepresentation ?? throw new InvalidOperationException("Tool approval request content is unavailable.");

        using (generationCommandScopeFactory.Create(DenyToolApprovalCommand, null))
        {
            await RespondToToolApprovalAsync(request, runtimeContent.CreateResponse(false, "Rejected by user"), cancellationToken);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, true))
        {
            return;
        }

        IAsyncRelayCommand? command = GenerationCommand;
        GenerationCommand = null;
        command?.Cancel();
    }

    public void UpdateConversationStatistics()
    {
        ConversationStatistics = AgentConversationStatisticsViewModel.Create(Messages);
    }

    internal void UpdateTurnMetadata(string? titleInput)
    {
        UpdatedAt = DateTimeOffset.Now;
        if (titleInput is not null && string.IsNullOrWhiteSpace(Title))
        {
            Title = CreateTitle(titleInput);
        }
    }

    private bool CanRespondToToolApproval(ObservableToolApprovalRequestContent? request)
    {
        return !disposed && GenerationCommand is null && AgentConversationTurnController.CanRespondToToolApproval(this, request);
    }

    private Task RespondToToolApprovalAsync(ObservableToolApprovalRequestContent request, ToolApprovalResponseContent responseContent, CancellationToken cancellationToken)
    {
        if (disposed || !ReferenceEquals(ToolApprovalRequest, request))
        {
            return Task.CompletedTask;
        }

        return conversationTurnController.RespondToToolApprovalAsync(this, responseContent, cancellationToken);
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
}
