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

    private IAsyncRelayCommand? generationCommand;
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
    [JsonIgnore]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial ModelProfile? ModelProfile { get; set; }

    partial void OnModelProfileChanged(ModelProfile? value)
    {
        Runtime.Reset();
    }

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

    private bool CanSendMessage { get => !disposed && generationCommand is null && !IsBusy && ModelProviderProfile is not null && !string.IsNullOrWhiteSpace(InputText) && !string.IsNullOrWhiteSpace(ModelProfile?.ModelId); }

    [JsonIgnore]
    public bool CanStopGeneration { get => !disposed && generationCommand is { IsCancellationRequested: false }; }

    private bool CanDeleteConversation { get => !disposed && generationCommand is null && !IsBusy; }

    internal AgentConversationCommandNotifier CommandNotifier { get => field ??= new(this); }

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

        try
        {
            InputText = string.Empty;
            await RunGenerationCommandAsync(SendMessageCommand, token => conversationTurnController.SendMessageAsync(this, input, ConfigureObservableContent, token), cancellationToken);
        }
        catch (AgentConversationException ex)
        {
            InputText = string.Empty;
            AddExceptionMessages(input, ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopGeneration))]
    private void StopGeneration()
    {
        if (disposed)
        {
            return;
        }

        IAsyncRelayCommand? command = generationCommand;
        if (command is null)
        {
            return;
        }

        SentryDiagnostics.AddBreadcrumb("Stop chat generation", SentryBreadcrumbCategories.AIChat, SentryBreadcrumbTypes.UI);
        command.Cancel();
        OnPropertyChanged(nameof(CanStopGeneration));
        CommandNotifier.NotifyStopGenerationChanged();
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
        ToolApprovalRequestContent runtimeContent = request.RawRepresentation ?? throw new InvalidOperationException("Tool approval request content is unavailable.");
        await RunGenerationCommandAsync(ApproveToolApprovalCommand, token => RespondToToolApprovalAsync(request, runtimeContent.CreateResponse(true), token), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRespondToToolApproval))]
    private async Task DenyToolApprovalAsync(ObservableToolApprovalRequestContent request, CancellationToken cancellationToken)
    {
        ToolApprovalRequestContent runtimeContent = request.RawRepresentation ?? throw new InvalidOperationException("Tool approval request content is unavailable.");
        await RunGenerationCommandAsync(DenyToolApprovalCommand, token => RespondToToolApprovalAsync(request, runtimeContent.CreateResponse(false, "Rejected by user"), token), cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, true))
        {
            return;
        }

        SetGenerationCommand(null)?.Cancel();
        CommandNotifier.NotifyDeleteConversationChanged();
    }

    public void RebuildConversationStatistics()
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
        return !disposed && generationCommand is null && conversationTurnController.CanRespondToToolApproval(this, request, out _, out _, out _);
    }

    private Task RespondToToolApprovalAsync(ObservableToolApprovalRequestContent request, AIContent responseContent, CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }

        return conversationTurnController.RespondToToolApprovalAsync(this, request, responseContent, ConfigureObservableContent, CommandNotifier.NotifyToolApprovalChanged, cancellationToken);
    }

    private async Task RunGenerationCommandAsync(IAsyncRelayCommand command, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        SetGenerationCommand(command);

        try
        {
            await operation(cancellationToken);
        }
        finally
        {
            SetGenerationCommand(null);
        }
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

    private IAsyncRelayCommand? SetGenerationCommand(IAsyncRelayCommand? value)
    {
        IAsyncRelayCommand? previous = Interlocked.Exchange(ref generationCommand, value);
        if (ReferenceEquals(previous, value))
        {
            return previous;
        }

        OnPropertyChanged(nameof(CanStopGeneration));
        CommandNotifier.NotifyGenerationCommandChanged();
        return previous;
    }

    private void AddExceptionMessages(string input, AgentConversationException ex)
    {
        ObservableChatMessage inputMessage = ObservableChatMessage.Create(ChatRole.User, DateTimeOffset.Now, "You", ObservableTextContent.Create(input));
        Messages.Add(inputMessage);

        ObservableErrorContent errorContent = ObservableErrorContent.Create(ex.Message);
        ObservableChatMessage exceptionMessage = ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, content: errorContent);
        Messages.Add(exceptionMessage);

        UpdateTurnMetadata(input);
        RebuildConversationStatistics();
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
}
