using System.Collections.Generic;
using System.Linq;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;

internal sealed class AgentWorkspaceGlobToolResult
{
    private const int MaximumGlobFileNameCount = 100;

    public required List<string> FileNames { get; init; }

    public required bool Truncated { get; init; }

    public static AgentWorkspaceGlobToolResult Create(IReadOnlyList<string> fileNames)
    {
        return new()
        {
            FileNames = [.. fileNames.Take(MaximumGlobFileNameCount)],
            Truncated = fileNames.Count > MaximumGlobFileNameCount,
        };
    }
}
