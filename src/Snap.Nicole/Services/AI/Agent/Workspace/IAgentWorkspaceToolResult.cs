using System.Diagnostics.CodeAnalysis;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal interface IAgentWorkspaceToolResult<TResult>
    where TResult : IAgentWorkspaceToolResult<TResult>
{
    static abstract bool TryAdd(string callId, TResult result);

    static abstract bool TryRemove(string callId, [MaybeNullWhen(false)] out TResult result);
}