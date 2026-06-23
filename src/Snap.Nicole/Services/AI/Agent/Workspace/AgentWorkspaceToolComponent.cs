using Microsoft.Extensions.AI;
using System.IO;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal abstract class AgentWorkspaceToolComponent(AgentWorkspaceContext context)
{
    protected static readonly EnumerationOptions DefaultEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true,
    };

    public abstract AITool Tool { get; }

    protected AgentWorkspaceContext Context { get; } = context;
}
