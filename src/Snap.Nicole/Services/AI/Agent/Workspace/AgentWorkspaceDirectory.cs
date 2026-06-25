namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceDirectory : AgentWorkspacePath
{
    public static AgentWorkspaceDirectory Create(string rootDirectory, string fullPath)
    {
        return new()
        {
            RootDirectory = rootDirectory,
            FullPath = fullPath,
        };
    }
}
