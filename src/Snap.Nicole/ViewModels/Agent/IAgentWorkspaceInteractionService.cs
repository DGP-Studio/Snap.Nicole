using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal interface IAgentWorkspaceInteractionService
{
    ValueTask<string?> PickExternalFolderAsync(string? currentFolderPath, CancellationToken cancellationToken);

    ValueTask<bool> ConfirmExternalFolderAsync(string folderPath, CancellationToken cancellationToken);

    void OpenFolder(string folderPath);
}
