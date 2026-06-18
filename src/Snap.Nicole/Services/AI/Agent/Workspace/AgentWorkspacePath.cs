namespace Snap.Nicole.Services.AI.Agent.Workspace;

public sealed class AgentWorkspacePath
{
    public required AgentWorkspaceRoot Root { get; init; }

    public required string RelativePath { get; init; }

    public required string FullPath { get; init; }

    public required string DisplayPath { get; init; }
}
