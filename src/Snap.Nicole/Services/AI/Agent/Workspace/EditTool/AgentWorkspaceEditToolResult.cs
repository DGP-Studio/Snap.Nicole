using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal sealed class AgentWorkspaceEditToolResult
{
    private static readonly ConcurrentDictionary<string, AgentWorkspaceEditToolResult> cache = [];

    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }

    [JsonPropertyName("oldString")]
    public required string OldString { get; init; }

    [JsonPropertyName("newString")]
    public required string NewString { get; init; }

    [JsonPropertyName("additions")]
    public required int Additions { get; init; }

    [JsonPropertyName("deletions")]
    public required int Deletions { get; init; }

    [JsonPropertyName("structuredPatch")]
    public required List<AgentWorkspaceEditToolHunk> StructuredPatch { get; init; }

    public static bool TryAdd(string callId, AgentWorkspaceEditToolResult result)
    {
        return cache.TryAdd(callId, result);
    }

    public static bool TryRemove(string callId, [MaybeNullWhen(false)] out AgentWorkspaceEditToolResult result)
    {
        return cache.TryRemove(callId, out result);
    }
}
