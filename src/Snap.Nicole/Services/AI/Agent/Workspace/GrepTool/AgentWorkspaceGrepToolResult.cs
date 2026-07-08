using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal sealed class AgentWorkspaceGrepToolResult
{
    private static readonly ConcurrentDictionary<string, AgentWorkspaceGrepToolResult> cache = [];

    [JsonPropertyName("searchPath")]
    public required string SearchPath { get; init; }

    [JsonPropertyName("outputMode")]
    public required AgentWorkspaceGrepToolOutputMode OutputMode { get; init; }

    [JsonPropertyName("lines")]
    public required List<string> Lines { get; init; }

    [JsonPropertyName("totalLineCount")]
    public required int TotalLineCount { get; init; }

    [JsonPropertyName("offset")]
    public required int Offset { get; init; }

    [JsonPropertyName("headLimit")]
    public required int HeadLimit { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    public static bool TryAdd(string callId, AgentWorkspaceGrepToolResult result)
    {
        return cache.TryAdd(callId, result);
    }

    public static bool TryRemove(string callId, [MaybeNullWhen(false)] out AgentWorkspaceGrepToolResult result)
    {
        return cache.TryRemove(callId, out result);
    }
}
