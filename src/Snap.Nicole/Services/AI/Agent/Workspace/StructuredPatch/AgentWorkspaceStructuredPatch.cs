using System.Collections.Generic;

namespace Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

internal sealed class AgentWorkspaceStructuredPatch
{
    public required int Additions { get; init; }

    public required int Deletions { get; init; }

    public required IReadOnlyList<AgentWorkspaceStructuredPatchHunk> Hunks { get; init; }
}
