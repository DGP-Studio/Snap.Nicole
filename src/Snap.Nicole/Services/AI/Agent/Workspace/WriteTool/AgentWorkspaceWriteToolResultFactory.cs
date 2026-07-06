using Microsoft.Extensions.AI;

namespace Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;

internal static class AgentWorkspaceWriteToolResultFactory
{
    public static TextContent Create(AgentWorkspaceFile file)
    {
        return new TextContent($"File '{file.FullPath}' written.");
    }
}
