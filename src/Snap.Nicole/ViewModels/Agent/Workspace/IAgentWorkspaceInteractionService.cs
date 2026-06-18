using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent.Workspace;

internal interface IAgentWorkspaceInteractionService
{
    ValueTask<IReadOnlyList<string>> PickExternalFoldersAsync(IReadOnlyList<string> currentFolderPaths, CancellationToken cancellationToken);

    ValueTask<bool> ConfirmExternalFoldersAsync(IReadOnlyList<string> folderPaths, CancellationToken cancellationToken);

    void OpenFolder(string folderPath);
}
