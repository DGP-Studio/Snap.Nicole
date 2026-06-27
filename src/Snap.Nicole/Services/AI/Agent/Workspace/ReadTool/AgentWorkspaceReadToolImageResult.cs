using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

internal sealed class AgentWorkspaceReadToolImageResult : AgentWorkspaceReadToolResult
{
    [JsonPropertyName("type")]
    public required string MediaType { get; init; }

    [JsonPropertyName("fileSize")]
    public required long FileSize { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }
}
