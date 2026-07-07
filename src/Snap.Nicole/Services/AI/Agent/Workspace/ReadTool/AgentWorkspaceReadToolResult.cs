using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

[JsonPolymorphic]
[JsonDerivedType(typeof(AgentWorkspaceReadToolTextResult), "text")]
[JsonDerivedType(typeof(AgentWorkspaceReadToolImageResult), "image")]
internal abstract class AgentWorkspaceReadToolResult
{
    private static readonly ConcurrentDictionary<string, AgentWorkspaceReadToolResult> cache = [];

    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }

    public static bool TryAdd(string callId, AgentWorkspaceReadToolResult result)
    {
        return cache.TryAdd(callId, result);
    }

    public static bool TryRemove(string callId, [MaybeNullWhen(false)] out AgentWorkspaceReadToolResult result)
    {
        return cache.TryRemove(callId, out result);
    }
}
