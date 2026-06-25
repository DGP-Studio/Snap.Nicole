namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal readonly struct AgentWorkspaceEditToolMatch
{
    public AgentWorkspaceEditToolMatch(long startIndex)
    {
        StartIndex = startIndex;
    }

    public long StartIndex { get; }
}
