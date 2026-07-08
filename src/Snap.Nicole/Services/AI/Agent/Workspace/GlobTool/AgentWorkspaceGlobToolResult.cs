using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;

internal sealed class AgentWorkspaceGlobToolResult
{
    private static readonly ConcurrentDictionary<string, AgentWorkspaceGlobToolResult> cache = [];

    [JsonPropertyName("fileNames")]
    public required List<string> FileNames { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    public static bool TryAdd(string callId, AgentWorkspaceGlobToolResult result)
    {
        return cache.TryAdd(callId, result);
    }

    public static bool TryRemove(string callId, [MaybeNullWhen(false)] out AgentWorkspaceGlobToolResult result)
    {
        return cache.TryRemove(callId, out result);
    }
}
