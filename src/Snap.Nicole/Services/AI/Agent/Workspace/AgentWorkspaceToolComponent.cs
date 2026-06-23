using Microsoft.Extensions.AI;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal abstract class AgentWorkspaceToolComponent
{
    protected AgentWorkspaceToolComponent(AgentWorkspaceContext context)
    {
        Context = context;
    }

    public abstract AITool Tool { get; }

    protected AgentWorkspaceContext Context { get; }
}
