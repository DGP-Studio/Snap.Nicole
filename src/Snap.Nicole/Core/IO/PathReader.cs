using System.IO;

namespace Snap.Nicole.Core.IO;

internal ref struct PathReader(string path)
{
    private readonly string path = path;

    private int position = 0;
    private bool pendingEmptySegment = false;

    public bool TryReadSegment(out string segment, out bool hasNextSegment)
    {
        if (position == path.Length)
        {
            if (!pendingEmptySegment)
            {
                segment = string.Empty;
                hasNextSegment = false;
                return false;
            }

            pendingEmptySegment = false;
            segment = string.Empty;
            hasNextSegment = false;
            return true;
        }

        ReadOnlySpan<char> remainingPath = path.AsSpan(position);
        int separatorIndex = remainingPath.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (separatorIndex < 0)
        {
            segment = path[position..];
            position = path.Length;
            hasNextSegment = false;
            return true;
        }

        segment = path.Substring(position, separatorIndex);
        position += separatorIndex + 1;
        pendingEmptySegment = position == path.Length;
        hasNextSegment = pendingEmptySegment || position < path.Length;
        return true;
    }
}
