using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

internal sealed class AgentWorkspaceReadToolFileUnchangedResult : AgentWorkspaceReadToolResult
{
    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }
}