using System.IO;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceRoot
{
    public required string Directory { get; init; }

    public string GetRelativePath(string fullPath)
    {
        return Path.GetRelativePath(Directory, fullPath).Replace('\\', '/');
    }
}
