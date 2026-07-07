using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

internal sealed class AgentWorkspaceReadToolImageResult : AgentWorkspaceReadToolResult
{
    [JsonPropertyName("type")]
    public required string MediaType { get; init; }

    [JsonPropertyName("fileSize")]
    public required long FileSize { get; init; }
}
