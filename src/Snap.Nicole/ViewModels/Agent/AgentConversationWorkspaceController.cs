using Microsoft.Extensions.AI;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationWorkspaceController(IServiceProvider serviceProvider)
{
    private readonly AgentWorkspaceProvider workspaceProvider = serviceProvider.GetRequiredService<AgentWorkspaceProvider>();
    private readonly AgentConversationPersistenceController persistenceController = serviceProvider.GetRequiredService<AgentConversationPersistenceController>();
    private readonly IAgentWorkspaceInteractionService interactionService = serviceProvider.GetRequiredService<IAgentWorkspaceInteractionService>();

    public StringResourceValue GetWorkspaceDisplayName(AgentConversationViewModel conversation)
    {
        AgentConversationWorkspace workspace = conversation.Workspace;
        if (workspace.Kind is not AgentWorkspaceKind.ExternalFolder || workspace.ExternalFolderPaths.Count is 0)
        {
            return SRName.UIXamlPagesAgentPageLabelDefaultWorkspace;
        }

        if (workspace.ExternalFolderPaths.Count > 1)
        {
            return StringResourceValue.FromName(SRName.UIXamlPagesAgentPageLabelMultipleWorkspaces, workspace.ExternalFolderPaths.Count);
        }

        string displayPath = GetDisplayPath(workspace.ExternalFolderPaths[0]);
        string? name = Path.GetFileName(displayPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? StringResourceValue.FromText(displayPath) : StringResourceValue.FromText(name);
    }

    public string GetWorkspaceDisplayPath(AgentConversationViewModel conversation)
    {
        AgentConversationWorkspace workspace = conversation.Workspace;
        if (workspace.Kind is not AgentWorkspaceKind.ExternalFolder)
        {
            return workspaceProvider.GetAppManagedWorkingDirectory(conversation.Id);
        }

        if (workspace.ExternalFolderPaths.Count is 0)
        {
            return string.Empty;
        }

        List<string> displayPaths = [];
        foreach (string path in workspace.ExternalFolderPaths)
        {
            displayPaths.Add(GetDisplayPath(path));
        }

        return string.Join(Environment.NewLine, displayPaths);
    }

    public async Task SelectExternalWorkspaceAsync(AgentConversationViewModel conversation, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> selectedFolderPaths = await interactionService.PickExternalFoldersAsync(conversation.Workspace.ExternalFolderPaths, cancellationToken);
        if (selectedFolderPaths.Count is 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<string> normalizedPaths = workspaceProvider.NormalizeExternalFolderPaths(selectedFolderPaths);
            bool confirmed = await interactionService.ConfirmExternalFoldersAsync(normalizedPaths, cancellationToken);
            if (!confirmed)
            {
                return;
            }

            SetWorkspace(conversation, new()
            {
                Kind = AgentWorkspaceKind.ExternalFolder,
                ExternalFolderPaths = [.. normalizedPaths],
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (AgentWorkspaceProvider.IsWorkspaceException(ex))
        {
            AddWorkspaceError(conversation, ex.Message);
        }
    }

    public void ResetWorkspace(AgentConversationViewModel conversation)
    {
        SetWorkspace(conversation, new());
    }

    public void OpenWorkspace(AgentConversationViewModel conversation)
    {
        try
        {
            AgentWorkspaceSnapshot workspace = workspaceProvider.CreateSnapshot(conversation.Id, conversation.Workspace);
            foreach (string workingDirectory in workspace.WorkingDirectories)
            {
                interactionService.OpenFolder(workingDirectory);
            }
        }
        catch (Exception ex) when (AgentWorkspaceProvider.IsWorkspaceException(ex) || ex is Win32Exception)
        {
            AddWorkspaceError(conversation, ex.Message);
        }
    }

    private void SetWorkspace(AgentConversationViewModel conversation, AgentConversationWorkspace workspace)
    {
        conversation.Workspace = workspace;
        conversation.UpdatedAt = DateTimeOffset.Now;
        persistenceController.SaveConversation(conversation);
    }

    private static string GetDisplayPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (AgentWorkspaceProvider.IsWorkspaceException(ex))
        {
            return path;
        }
    }

    private static void AddWorkspaceError(AgentConversationViewModel conversation, string message)
    {
        ObservableErrorContent errorContent = ObservableErrorContent.Create(message);
        ObservableChatMessage errorMessage = ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, content: errorContent);
        conversation.Messages.Add(errorMessage);
        conversation.UpdateConversationStatistics();
    }
}
