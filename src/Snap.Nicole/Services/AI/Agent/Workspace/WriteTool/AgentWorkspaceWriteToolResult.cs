using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

namespace Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;

internal sealed class AgentWorkspaceWriteToolResult : IAgentWorkspaceToolResult<AgentWorkspaceWriteToolResult>
{
    private static readonly ConcurrentDictionary<string, AgentWorkspaceWriteToolResult> cache = [];

    [JsonPropertyName("type")]
    public required AgentWorkspaceWriteToolResultType Type { get; init; }

    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }

    [JsonPropertyName("additions")]
    public required int Additions { get; init; }

    [JsonPropertyName("deletions")]
    public required int Deletions { get; init; }

    [JsonPropertyName("structuredPatch")]
    public required IReadOnlyList<AgentWorkspaceStructuredPatchHunk> StructuredPatch { get; init; }

    [JsonPropertyName("originalFile")]
    public required string? OriginalFile { get; init; }

    public static bool TryAdd(string callId, AgentWorkspaceWriteToolResult result)
    {
        return cache.TryAdd(callId, result);
    }

    public static bool TryRemove(string callId, [MaybeNullWhen(false)] out AgentWorkspaceWriteToolResult result)
    {
        return cache.TryRemove(callId, out result);
    }
}
