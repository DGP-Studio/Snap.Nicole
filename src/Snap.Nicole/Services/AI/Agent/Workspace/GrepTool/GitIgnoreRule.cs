using System.Text;
using System.Text.RegularExpressions;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal sealed class GitIgnoreRule
{
    public required Regex ExactPathRegex { get; init; }

    public required Regex DescendantPathRegex { get; init; }

    public required bool IsNegated { get; init; }

    public required bool DirectoryOnly { get; init; }

    public static GitIgnoreRule? Create(string baseRelativeDirectory, string line)
    {
        string pattern = TrimUnescapedTrailingWhiteSpace(line);
        if (pattern.Length is 0 || pattern[0] is '#')
        {
            return null;
        }

        bool isNegated = pattern[0] is '!';
        if (isNegated)
        {
            pattern = pattern[1..];
            if (pattern.Length is 0)
            {
                return null;
            }
        }

        bool directoryOnly = pattern.EndsWith("/", StringComparison.Ordinal);
        if (directoryOnly)
        {
            pattern = pattern.TrimEnd('/');
            if (pattern.Length is 0)
            {
                return null;
            }
        }

        bool anchored = pattern.StartsWith("/", StringComparison.Ordinal);
        pattern = pattern.TrimStart('/');
        if (pattern.Length is 0)
        {
            return null;
        }

        anchored = anchored || pattern.Contains("/", StringComparison.Ordinal);

        return new()
        {
            ExactPathRegex = CreatePathRegex(baseRelativeDirectory, pattern, anchored, descendant: false),
            DescendantPathRegex = CreatePathRegex(baseRelativeDirectory, pattern, anchored, descendant: true),
            IsNegated = isNegated,
            DirectoryOnly = directoryOnly,
        };
    }

    public bool Matches(string rootRelativePath, bool isDirectory)
    {
        if (DirectoryOnly)
        {
            return isDirectory && ExactPathRegex.IsMatch(rootRelativePath) || DescendantPathRegex.IsMatch(rootRelativePath);
        }

        return ExactPathRegex.IsMatch(rootRelativePath) || DescendantPathRegex.IsMatch(rootRelativePath);
    }

    private static Regex CreatePathRegex(string baseRelativeDirectory, string pattern, bool anchored, bool descendant)
    {
        StringBuilder builder = new();
        builder.Append('^');
        if (!string.IsNullOrEmpty(baseRelativeDirectory))
        {
            AppendPathLiteralRegex(builder, baseRelativeDirectory);
            builder.Append('/');
        }

        if (!anchored)
        {
            builder.Append("(?:.*/)?");
        }

        AppendGlobRegex(builder, pattern);
        if (descendant)
        {
            builder.Append("/.*");
        }

        builder.Append('$');
        return new(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(10));
    }

    private static void AppendPathLiteralRegex(StringBuilder builder, string path)
    {
        for (int i = 0; i < path.Length; i++)
        {
            char value = path[i];
            if (value is '/')
            {
                builder.Append('/');
                continue;
            }

            builder.Append(Regex.Escape(value.ToString()));
        }
    }

    private static void AppendGlobRegex(StringBuilder builder, string pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            char value = pattern[i];
            if (value is '\\' && i + 1 < pattern.Length)
            {
                i++;
                AppendRegexLiteral(builder, pattern[i]);
                continue;
            }

            if (value is '*')
            {
                AppendStarRegex(builder, pattern, ref i);
                continue;
            }

            if (value is '?')
            {
                builder.Append("[^/]");
                continue;
            }

            if (value is '[' && TryAppendCharacterClassRegex(builder, pattern, ref i))
            {
                continue;
            }

            AppendRegexLiteral(builder, value);
        }
    }

    private static void AppendStarRegex(StringBuilder builder, string pattern, ref int index)
    {
        if (index + 1 >= pattern.Length || pattern[index + 1] is not '*')
        {
            builder.Append("[^/]*");
            return;
        }

        bool precededBySlash = index is 0 || pattern[index - 1] is '/';
        bool followedBySlash = index + 2 < pattern.Length && pattern[index + 2] is '/';
        if (precededBySlash && followedBySlash)
        {
            builder.Append("(?:[^/]+/)*");
            index += 2;
            return;
        }

        builder.Append(".*");
        index++;
    }

    private static void AppendRegexLiteral(StringBuilder builder, char value)
    {
        if (value is '/')
        {
            builder.Append('/');
            return;
        }

        builder.Append(Regex.Escape(value.ToString()));
    }

    private static bool TryAppendCharacterClassRegex(StringBuilder builder, string pattern, ref int index)
    {
        int endIndex = index + 1;
        if (endIndex < pattern.Length && pattern[endIndex] is '!' or '^')
        {
            endIndex++;
        }

        if (endIndex < pattern.Length && pattern[endIndex] is ']')
        {
            endIndex++;
        }

        while (endIndex < pattern.Length && pattern[endIndex] is not ']')
        {
            endIndex++;
        }

        if (endIndex >= pattern.Length)
        {
            return false;
        }

        builder.Append('[');
        int startIndex = index + 1;
        if (pattern[startIndex] is '!')
        {
            builder.Append('^');
            startIndex++;
        }

        for (int i = startIndex; i < endIndex; i++)
        {
            char value = pattern[i];
            if (value is '\\' && i + 1 < endIndex)
            {
                i++;
                value = pattern[i];
            }

            if (value is '\\' or '^')
            {
                builder.Append('\\');
            }

            builder.Append(value);
        }

        builder.Append(']');
        index = endIndex;
        return true;
    }

    private static string TrimUnescapedTrailingWhiteSpace(string line)
    {
        int endIndex = line.Length;
        while (endIndex > 0 && char.IsWhiteSpace(line[endIndex - 1]) && !IsEscaped(line, endIndex - 1))
        {
            endIndex--;
        }

        return line[..endIndex];
    }

    private static bool IsEscaped(string value, int index)
    {
        int slashCount = 0;
        for (int i = index - 1; i >= 0 && value[i] is '\\'; i--)
        {
            slashCount++;
        }

        return slashCount % 2 is 1;
    }
}
