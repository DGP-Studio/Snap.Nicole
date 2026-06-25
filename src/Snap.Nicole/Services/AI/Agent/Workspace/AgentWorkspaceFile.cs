namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFile : AgentWorkspacePath
{
    public static AgentWorkspaceFile Create(string rootDirectory, string fullPath)
    {
        return new()
        {
            RootDirectory = rootDirectory,
            FullPath = fullPath,
        };
    }
}
