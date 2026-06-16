using Microsoft.Windows.Storage.Pickers;
using Microsoft.UI.Xaml.Controls;
using Snap.Nicole.Core.Hosting;
using Snap.Nicole.Resources;
using Snap.Nicole.UI.Xaml.Windows;
using Snap.Nicole.ViewModels.Agent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.UI.Xaml;

internal sealed class AgentWorkspaceInteractionService(IWindowLifeTime<MainWindow> mainWindowLifeTime) : IAgentWorkspaceInteractionService
{
    public async ValueTask<IReadOnlyList<string>> PickExternalFoldersAsync(IReadOnlyList<string> currentFolderPaths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MainWindow window = GetMainWindow();
        FolderPicker picker = new(window.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };

        if (currentFolderPaths.Count > 0 && Directory.Exists(currentFolderPaths[0]))
        {
            picker.SuggestedFolder = currentFolderPaths[0];
        }

        IReadOnlyList<PickFolderResult> folders = await picker.PickMultipleFoldersAsync();
        cancellationToken.ThrowIfCancellationRequested();

        List<string> paths = [];
        foreach (PickFolderResult folder in folders)
        {
            if (!string.IsNullOrWhiteSpace(folder.Path))
            {
                paths.Add(folder.Path);
            }
        }

        return paths;
    }

    public async ValueTask<bool> ConfirmExternalFoldersAsync(IReadOnlyList<string> folderPaths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MainWindow window = GetMainWindow();
        string content = string.Join(Environment.NewLine, folderPaths);
        ContentDialog dialog = new()
        {
            XamlRoot = window.Content.XamlRoot,
            Title = StringResourceProxy.Default[SRName.UIXamlPagesAgentPageMessageExternalWorkspaceConfirmTitle],
            Content = StringResourceValue.FromName(SRName.UIXamlPagesAgentPageMessageExternalWorkspaceConfirmContent, content).Value,
            PrimaryButtonText = StringResourceProxy.Default[SRName.UIXamlPagesAgentPageLabelConfirmWorkspace],
            CloseButtonText = StringResourceProxy.Default[SRName.UIXamlPagesAgentPageLabelCancelWorkspace],
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result is ContentDialogResult.Primary;
    }

    public void OpenFolder(string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true,
        });
    }

    private MainWindow GetMainWindow()
    {
        mainWindowLifeTime.Show();
        return mainWindowLifeTime.Window ?? throw new InvalidOperationException("Main window is unavailable.");
    }
}
