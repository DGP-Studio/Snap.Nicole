using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

internal sealed class AgentWorkspaceStructuredPatchHunk
{
    [JsonPropertyName("lines")]
    public required IReadOnlyList<AgentWorkspaceStructuredPatchHunkLine> Lines { get; init; }

    public static AgentWorkspaceStructuredPatchHunk Create(IReadOnlyList<AgentWorkspaceStructuredPatchHunkLine> lines)
    {
        return new()
        {
            Lines = lines,
        };
    }
}
