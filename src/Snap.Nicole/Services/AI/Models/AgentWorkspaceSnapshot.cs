using Snap.Nicole.Core.IO;
using System.Collections.Generic;

namespace Snap.Nicole.Services.AI.Models;

internal sealed record AgentWorkspaceSnapshot
{
    public required string WorkingDirectory { get; init; }

    public required IReadOnlyList<string> WorkingDirectories { get; init; }

    public required string MemoryDirectory { get; init; }

    public bool AgentEquals(AgentWorkspaceSnapshot? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return PathEquals(WorkingDirectory, other.WorkingDirectory)
            && PathEquals(MemoryDirectory, other.MemoryDirectory)
            && PathSequenceEquals(WorkingDirectories, other.WorkingDirectories);
    }

    private static bool PathEquals(string? x, string? y)
    {
        return FileSystemPath.Equals(x, y);
    }

    private static bool PathSequenceEquals(IReadOnlyList<string> x, IReadOnlyList<string> y)
    {
        if (x.Count != y.Count)
        {
            return false;
        }

        for (int i = 0; i < x.Count; i++)
        {
            if (!PathEquals(x[i], y[i]))
            {
                return false;
            }
        }

        return true;
    }
}
