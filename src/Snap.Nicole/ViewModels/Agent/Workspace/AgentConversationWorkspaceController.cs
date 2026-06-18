using Microsoft.Extensions.AI;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI.Agent.Workspace;
using Snap.Nicole.Services.AI.Observables;
using Snap.Nicole.ViewModels.Agent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent.Workspace;

internal sealed class AgentConversationWorkspaceController(IServiceProvider serviceProvider)
{
    private readonly AgentWorkspaceProvider workspaceProvider = serviceProvider.GetRequiredService<AgentWorkspaceProvider>();
    private readonly AgentConversationPersistenceController persistenceController = serviceProvider.GetRequiredService<AgentConversationPersistenceController>();
    private readonly IAgentWorkspaceInteractionService interactionService = serviceProvider.GetRequiredService<IAgentWorkspaceInteractionService>();

    public StringResourceValue GetWorkspaceDisplayName(AgentConversationViewModel conversation)
    {
        if (conversation.ExternalDirectories.Count is 0)
        {
            return SRName.UIXamlPagesAgentPageLabelDefaultWorkspace;
        }

        if (conversation.ExternalDirectories.Count > 1)
        {
            return StringResourceValue.FromName(SRName.UIXamlPagesAgentPageLabelMultipleWorkspaces, conversation.ExternalDirectories.Count);
        }

        string displayPath = GetDisplayPath(conversation.ExternalDirectories[0]);
        string? name = Path.GetFileName(displayPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? StringResourceValue.FromText(displayPath) : StringResourceValue.FromText(name);
    }

    public string GetWorkspaceDisplayPath(AgentConversationViewModel conversation)
    {
        List<string> displayPaths = [GetDisplayPath(workspaceProvider.GetAppManagedWorkingDirectory(conversation.Id))];
        foreach (string path in conversation.ExternalDirectories)
        {
            displayPaths.Add(GetDisplayPath(path));
        }

        return string.Join(Environment.NewLine, displayPaths);
    }

    public async Task SelectExternalWorkspaceAsync(AgentConversationViewModel conversation, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> selectedDirectories = await interactionService.PickExternalFoldersAsync(conversation.ExternalDirectories, cancellationToken);
        if (selectedDirectories.Count is 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<string> normalizedDirectories = workspaceProvider.NormalizeExternalDirectories(selectedDirectories);
            bool confirmed = await interactionService.ConfirmExternalFoldersAsync(normalizedDirectories, cancellationToken);
            if (!confirmed)
            {
                return;
            }

            SetExternalDirectories(conversation, normalizedDirectories);
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
        SetExternalDirectories(conversation, []);
    }

    public void OpenWorkspace(AgentConversationViewModel conversation)
    {
        try
        {
            AgentWorkspaceSnapshot workspace = workspaceProvider.CreateSnapshot(conversation.Id, conversation.ExternalDirectories);
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

    private void SetExternalDirectories(AgentConversationViewModel conversation, IReadOnlyList<string> externalDirectories)
    {
        conversation.ExternalDirectories = [.. externalDirectories];
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
