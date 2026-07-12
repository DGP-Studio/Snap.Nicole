using Snap.Nicole.Core.IO;
using System.IO;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceRootDirectory(string fullPath)
{
    public string FullPath { get; } = Path.TrimEndingDirectorySeparator(fullPath);

    public bool Contains(string fullPath)
    {
        return Path.IsEqualOrSubdirectory(FullPath, fullPath);
    }

    public string GetRelativePath(string path)
    {
        return Path.GetRelativePath(FullPath, path).Replace('\\', '/');
    }
}
