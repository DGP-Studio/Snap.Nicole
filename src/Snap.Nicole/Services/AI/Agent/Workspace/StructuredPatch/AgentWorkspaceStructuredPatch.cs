using System.Collections.Generic;

namespace Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

internal sealed class AgentWorkspaceStructuredPatch
{
    public static AgentWorkspaceStructuredPatch Empty { get; } = new()
    {
        Additions = 0,
        Deletions = 0,
        Hunks = [],
    };

    public required int Additions { get; init; }

    public required int Deletions { get; init; }

    public required IReadOnlyList<AgentWorkspaceStructuredPatchHunk> Hunks { get; init; }
}
