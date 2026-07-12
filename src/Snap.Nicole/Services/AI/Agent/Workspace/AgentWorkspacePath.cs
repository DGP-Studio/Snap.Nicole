using System.IO;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal abstract class AgentWorkspacePath
{
    public required AgentWorkspaceRootDirectory RootDirectory { get; init; }

    public required string FullPath { get; init; }

    public string GetRelativePath(AgentWorkspacePath path)
    {
        return GetRelativePath(path.FullPath);
    }

    public string GetRelativePath(string path)
    {
        return Path.GetRelativePath(FullPath, path).Replace('\\', '/');
    }
}
