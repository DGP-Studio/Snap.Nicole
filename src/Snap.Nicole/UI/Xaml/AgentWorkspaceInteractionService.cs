using Microsoft.UI.Xaml.Controls;
using Snap.Nicole.Core.Hosting;
using Snap.Nicole.Resources;
using Snap.Nicole.UI.Xaml.Windows;
using Snap.Nicole.ViewModels.Agent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Snap.Nicole.UI.Xaml;

internal sealed class AgentWorkspaceInteractionService(IWindowLifeTime<MainWindow> mainWindowLifeTime) : IAgentWorkspaceInteractionService
{
    public async ValueTask<string?> PickExternalFolderAsync(string? currentFolderPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MainWindow window = GetMainWindow();
        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");

        if (!string.IsNullOrWhiteSpace(currentFolderPath) && Directory.Exists(currentFolderPath))
        {
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        }

        InitializeWithWindow.Initialize(picker, (nint)window.WindowHandle);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return folder?.Path;
    }

    public async ValueTask<bool> ConfirmExternalFolderAsync(string folderPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MainWindow window = GetMainWindow();
        ContentDialog dialog = new()
        {
            XamlRoot = window.Content.XamlRoot,
            Title = StringResourceProxy.Default[SRName.UIXamlPagesAgentPageMessageExternalWorkspaceConfirmTitle],
            Content = StringResourceValue.FromName(SRName.UIXamlPagesAgentPageMessageExternalWorkspaceConfirmContent, folderPath).Value,
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
