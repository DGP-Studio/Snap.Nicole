using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.IO;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal abstract class AgentWorkspaceTool(AgentWorkspaceContext context)
{
    protected static readonly EnumerationOptions DefaultEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true,
    };

    public abstract AITool Tool { get; }

    protected static string CallId
    {
        get
        {
            Debug.Assert(FunctionInvokingChatClient.CurrentContext is not null);
            return FunctionInvokingChatClient.CurrentContext.CallContent.CallId;
        }
    }

    protected AgentWorkspaceContext Context { get; } = context;
}
