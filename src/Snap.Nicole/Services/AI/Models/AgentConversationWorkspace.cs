namespace Snap.Nicole.Services.AI.Models;

internal sealed class AgentConversationWorkspace
{
    public AgentWorkspaceKind Kind { get; set; }

    public string? ExternalFolderPath { get; set; }

    public AgentConversationWorkspace Clone()
    {
        return new()
        {
            Kind = Kind,
            ExternalFolderPath = ExternalFolderPath,
        };
    }
}
