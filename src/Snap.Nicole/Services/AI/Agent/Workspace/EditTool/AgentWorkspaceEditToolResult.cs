using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal sealed class AgentWorkspaceEditToolResult : IAgentWorkspaceToolResult<AgentWorkspaceEditToolResult>
{
    private static readonly ConcurrentDictionary<string, AgentWorkspaceEditToolResult> cache = [];

    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }

    [JsonPropertyName("additions")]
    public required int Additions { get; init; }

    [JsonPropertyName("deletions")]
    public required int Deletions { get; init; }

    [JsonPropertyName("structuredPatch")]
    public required IReadOnlyList<AgentWorkspaceStructuredPatchHunk> StructuredPatch { get; init; }

    public static bool TryAdd(string callId, AgentWorkspaceEditToolResult result)
    {
        return cache.TryAdd(callId, result);
    }

    public static bool TryRemove(string callId, [MaybeNullWhen(false)] out AgentWorkspaceEditToolResult result)
    {
        return cache.TryRemove(callId, out result);
    }
}
