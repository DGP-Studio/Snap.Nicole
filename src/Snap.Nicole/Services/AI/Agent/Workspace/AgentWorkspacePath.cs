using System.IO;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspacePath
{
    public required string RootDirectory { get; init; }

    public required string FullPath { get; init; }

    public static string GetRelativePath(string relativeTo, string path)
    {
        return Path.GetRelativePath(relativeTo, path).Replace('\\', '/');
    }

    public string GetRelativePath(string path)
    {
        return GetRelativePath(FullPath, path);
    }

    public string GetRootRelativePath(string path)
    {
        return GetRelativePath(RootDirectory, path);
    }
}
