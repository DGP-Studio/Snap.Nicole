using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal sealed class AgentWorkspaceGrepToolResult
{
    private static readonly ConcurrentDictionary<string, AgentWorkspaceGrepToolResult> cache = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mode")]
    public AgentWorkspaceGrepToolOutputMode? Mode { get; init; }

    [JsonPropertyName("numFiles")]
    public required int NumberOfFiles { get; init; }

    [JsonPropertyName("filenames")]
    public required List<string> FileNames { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("numLines")]
    public int? NumberOfLines { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("numMatches")]
    public int? NumberOfMatches { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("appliedLimit")]
    public int? AppliedLimit { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("appliedOffset")]
    public int? AppliedOffset { get; init; }

    public static bool TryAdd(string callId, AgentWorkspaceGrepToolResult result)
    {
        return cache.TryAdd(callId, result);
    }

    public static bool TryRemove(string callId, [MaybeNullWhen(false)] out AgentWorkspaceGrepToolResult result)
    {
        return cache.TryRemove(callId, out result);
    }
}
