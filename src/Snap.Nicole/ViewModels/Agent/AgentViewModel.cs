using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Snap.Nicole.Core.Collections.ObjectModel;
using Snap.Nicole.Services.Settings;
using System.Threading;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed partial class AgentViewModel : ObservableObject, IDisposable
{
    private readonly AgentConversationCollectionController conversationCollectionController;

    private bool disposed;

    public AgentViewModel(ISettingsProvider<AppSettings> settingsProvider, AgentConversationCollectionController conversationCollectionController)
    {
        Settings = settingsProvider.CurrentValue;
        this.conversationCollectionController = conversationCollectionController;
        this.conversationCollectionController.LoadConversations();
    }

    public AppSettings Settings { get; }

    public AdvancedObservableCollection<AgentConversationViewModel> Conversations { get => conversationCollectionController.Conversations; }

    [RelayCommand]
    private void CreateConversation()
    {
        if (disposed)
        {
            return;
        }

        conversationCollectionController.CreateConversation();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, true))
        {
            return;
        }

        conversationCollectionController.Dispose();
    }
}
