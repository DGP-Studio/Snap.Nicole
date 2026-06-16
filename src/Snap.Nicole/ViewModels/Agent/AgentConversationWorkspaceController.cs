using Microsoft.Extensions.AI;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
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
        if (workspace.Kind is not AgentWorkspaceKind.ExternalFolder || string.IsNullOrWhiteSpace(workspace.ExternalFolderPath))
        {
            return SRName.UIXamlPagesAgentPageLabelDefaultWorkspace;
        }

        string displayPath = GetDisplayPath(workspace.ExternalFolderPath);
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

        if (string.IsNullOrWhiteSpace(workspace.ExternalFolderPath))
        {
            return string.Empty;
        }

        return GetDisplayPath(workspace.ExternalFolderPath);
    }

    public async Task SelectExternalWorkspaceAsync(AgentConversationViewModel conversation, CancellationToken cancellationToken)
    {
        string? selectedFolderPath = await interactionService.PickExternalFolderAsync(conversation.Workspace.ExternalFolderPath, cancellationToken);
        if (selectedFolderPath is null)
        {
            return;
        }

        try
        {
            string normalizedPath = workspaceProvider.NormalizeExternalFolderPath(selectedFolderPath);
            bool confirmed = await interactionService.ConfirmExternalFolderAsync(normalizedPath, cancellationToken);
            if (!confirmed)
            {
                return;
            }

            SetWorkspace(conversation, new()
            {
                Kind = AgentWorkspaceKind.ExternalFolder,
                ExternalFolderPath = normalizedPath,
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
            interactionService.OpenFolder(workspace.WorkingDirectory);
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
