using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

public sealed class AgentWorkspaceException : Exception
{
    public AgentWorkspaceException(string message)
        : base(message)
    {
    }

    public AgentWorkspaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static AgentWorkspaceException Throw(string message)
    {
        throw new AgentWorkspaceException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static AgentWorkspaceException Throw(string message, Exception innerException)
    {
        throw new AgentWorkspaceException(message, innerException);
    }
}
