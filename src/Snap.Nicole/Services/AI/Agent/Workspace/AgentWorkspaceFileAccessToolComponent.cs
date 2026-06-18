using Microsoft.Extensions.AI;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal abstract class AgentWorkspaceFileAccessToolComponent
{
    protected AgentWorkspaceFileAccessToolComponent(AgentWorkspaceFileAccessContext context)
    {
        Context = context;
    }

    public abstract AITool Tool { get; }

    protected AgentWorkspaceFileAccessContext Context { get; }
}
