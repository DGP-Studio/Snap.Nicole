using System.IO;

namespace Snap.Nicole.Core.IO;

internal static class PathExtensions
{
    extension(Path)
    {
        public static bool IsEqual(string? left, string? right)
        {
            if (left is null || right is null)
            {
                return left is null && right is null;
            }

            int leftLength = GetLengthWithoutTrailingDirectorySeparator(left);
            int rightLength = GetLengthWithoutTrailingDirectorySeparator(right);
            return leftLength == rightLength && left.AsSpan(0, leftLength).Equals(right.AsSpan(0, rightLength), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsEqualOrSubdirectory(string directory, string path)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return true;
            }

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            int directoryLength = GetLengthWithoutTrailingDirectorySeparator(directory);
            int pathLength = GetLengthWithoutTrailingDirectorySeparator(path);
            if (pathLength < directoryLength)
            {
                return false;
            }

            if (!directory.AsSpan(0, directoryLength).Equals(path.AsSpan(0, directoryLength), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return pathLength <= directoryLength || IsDirectorySeparator(path[directoryLength]);
        }
    }

    private static int GetLengthWithoutTrailingDirectorySeparator(string path)
    {
        int length = path.Length;
        if (length > 0 && IsDirectorySeparator(path[length - 1]))
        {
            length--;
        }

        return length;
    }

    private static bool IsDirectorySeparator(char value)
    {
        return value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
    }
}
