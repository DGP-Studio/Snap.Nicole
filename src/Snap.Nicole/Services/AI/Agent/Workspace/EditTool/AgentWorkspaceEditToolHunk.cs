using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal sealed class AgentWorkspaceEditToolHunk
{
    [JsonPropertyName("lines")]
    public required IReadOnlyList<AgentWorkspaceEditToolHunkLine> Lines { get; init; }

    public static AgentWorkspaceEditToolHunk Create(IReadOnlyList<AgentWorkspaceEditToolHunkLine> lines)
    {
        return new AgentWorkspaceEditToolHunk
        {
            Lines = lines
        };
    }
}
