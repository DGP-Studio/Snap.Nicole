using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

internal sealed class AgentWorkspaceReadToolTextResult : AgentWorkspaceReadToolResult
{
    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }

    [JsonPropertyName("numLines")]
    public required int NumberOfLines { get; init; }

    [JsonPropertyName("startLine")]
    public required int StartLine { get; init; }

    [JsonPropertyName("totalLines")]
    public required int TotalLines { get; init; }
}
