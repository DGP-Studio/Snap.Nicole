namespace Snap.Nicole.Services.AI.Models;

internal sealed record AgentWorkspaceSnapshot
{
    public required AgentWorkspaceKind Kind { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string MemoryDirectory { get; init; }

    public string? ExternalFolderPath { get; init; }

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

        return Kind == other.Kind
            && PathEquals(WorkingDirectory, other.WorkingDirectory)
            && PathEquals(MemoryDirectory, other.MemoryDirectory)
            && PathEquals(ExternalFolderPath, other.ExternalFolderPath);
    }

    private static bool PathEquals(string? x, string? y)
    {
        return string.Equals(x, y, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
