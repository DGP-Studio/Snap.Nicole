using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal static class AgentWorkspaceEditToolResultStore
{
    private const int MaxCachedResultCount = 256;

    private static readonly Lock resultsLock = new();
    private static readonly Dictionary<string, AgentWorkspaceEditToolResult> resultsByCallId = new(StringComparer.Ordinal);
    private static readonly Queue<string> callIdOrder = new();

    public static void Store(string callId, AgentWorkspaceEditToolResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(result);

        lock (resultsLock)
        {
            if (!resultsByCallId.ContainsKey(callId))
            {
                callIdOrder.Enqueue(callId);
            }

            resultsByCallId[callId] = result;

            while (resultsByCallId.Count > MaxCachedResultCount && callIdOrder.TryDequeue(out string? expiredCallId))
            {
                resultsByCallId.Remove(expiredCallId);
            }
        }
    }

    public static bool TryGet(string callId, [MaybeNullWhen(false)] out AgentWorkspaceEditToolResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);

        lock (resultsLock)
        {
            return resultsByCallId.TryGetValue(callId, out result);
        }
    }
}
