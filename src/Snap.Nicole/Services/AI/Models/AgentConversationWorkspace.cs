using System.Collections.Generic;

namespace Snap.Nicole.Services.AI.Models;

internal sealed class AgentConversationWorkspace
{
    public AgentWorkspaceKind Kind { get; set; }

    public List<string> ExternalFolderPaths { get; set; } = [];

    public AgentConversationWorkspace Clone()
    {
        return new()
        {
            Kind = Kind,
            ExternalFolderPaths = [.. ExternalFolderPaths],
        };
    }
}
