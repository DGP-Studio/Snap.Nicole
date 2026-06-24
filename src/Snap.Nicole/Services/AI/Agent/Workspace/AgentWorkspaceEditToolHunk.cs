using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceEditToolHunk
{
    [JsonPropertyName("oldStart")]
    public required int OldStart { get; init; }

    [JsonPropertyName("oldLines")]
    public required int OldLines { get; init; }

    [JsonPropertyName("newStart")]
    public required int NewStart { get; init; }

    [JsonPropertyName("newLines")]
    public required int NewLines { get; init; }

    [JsonPropertyName("lines")]
    public required List<string> Lines { get; init; }
}
