using Snap.Nicole.Core;
using Snap.Nicole.Core.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal sealed class GitIgnoreRule
{
    public required Regex ExactPathRegex { get; init; }

    public required Regex DescendantPathRegex { get; init; }

    public required bool IsNegated { get; init; }

    public required bool DirectoryOnly { get; init; }

    // See https://git-scm.com/docs/gitignore
    public static GitIgnoreRule? Create(string rootRelativeDirectory, string line)
    {
        // Trailing spaces are ignored unless they are quoted with backslash ("\").
        string pattern = TrimUnescapedTrailingWhiteSpace(line);

        // A blank line matches no files, so it can serve as a separator for readability.
        // A line starting with # serves as a comment. Put a backslash ("\") in front of the first hash for patterns that begin with a hash.
        if (pattern.Length is 0 || pattern.StartsWith('#'))
        {
            return null;
        }

        // An optional prefix "!" which negates the pattern; any matching file excluded by a previous pattern will become included again.
        // It is not possible to re-include a file if a parent directory of that file is excluded.
        // Git doesn’t list excluded directories for performance reasons, so any patterns on contained files have no effect,
        // no matter where they are defined. Put a backslash ("\") in front of the first "!" for patterns that begin with a literal "!",
        // for example, "\!important!.txt".
        bool isNegated = pattern.StartsWith('!');
        if (isNegated)
        {
            pattern = pattern[1..];
            if (pattern.Length is 0)
            {
                return null;
            }
        }

        // If there is a separator at the end of the pattern then the pattern will only match directories,
        // otherwise the pattern can match both files and directories.
        bool directoryOnly = pattern.EndsWith('/');
        if (directoryOnly)
        {
            pattern = pattern[..^1];
            if (pattern.Length is 0)
            {
                return null;
            }
        }

        // If there is a separator at the beginning or middle (or both) of the pattern,
        // then the pattern is relative to the directory level of the particular .gitignore file itself.
        // Otherwise the pattern may also match at any level below the .gitignore level.
        bool anchored = pattern.Contains('/', StringComparison.Ordinal);
        if (pattern.StartsWith('/'))
        {
            pattern = pattern[1..];
            if (pattern.Length is 0)
            {
                return null;
            }
        }

        return new()
        {
            ExactPathRegex = CreatePathRegex(rootRelativeDirectory, pattern, anchored, false),
            DescendantPathRegex = CreatePathRegex(rootRelativeDirectory, pattern, anchored, true),
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

    private static Regex CreatePathRegex(string rootRelativeDirectory, string pattern, bool anchored, bool descendant)
    {
        // An asterisk "*" matches anything except a slash. The character "?" matches any one character except "/".
        // The range notation, e.g. [a-zA-Z], can be used to match one of the characters in a range.
        // A backslash ("\") can be used to escape any character. E.g., "\*" matches a literal asterisk (and "\a" matches "a", even though there is no need for escaping there).
        // A leading "**" followed by a slash means match in all directories.
        // For example, "**/foo" matches file or directory "foo" anywhere, the same as pattern "foo".
        // "**/foo/bar" matches file or directory "bar" anywhere that is directly under directory "foo".
        // A trailing "/**" matches everything inside. For example, "abc/**" matches all files inside directory "abc", relative to the location of the .gitignore file, with infinite depth.
        // A slash followed by two consecutive asterisks then a slash matches zero or more directories. For example, "a/**/b" matches "a/b", "a/x/b", "a/x/y/b" and so on.
        // Other consecutive asterisks are considered regular asterisks and will match according to the previous rules.
        StringBuilder builder = new();
        builder.Append('^');
        if (!string.IsNullOrEmpty(rootRelativeDirectory))
        {
            PathReader reader = new(rootRelativeDirectory);
            while (reader.TryReadSegment(out string segment, out bool hasNextSegment))
            {
                builder.Append(Regex.Escape(segment));
                if (hasNextSegment)
                {
                    builder.Append('/');
                }
            }

            builder.Append('/');
        }

        if (!anchored)
        {
            builder.Append("(?:.*/)?");
        }

        GitIgnorePatternParser.AppendRegex(builder, pattern);

        if (descendant)
        {
            builder.Append("/.*");
        }

        builder.Append('$');
        return new(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(10));
    }

    private static string TrimUnescapedTrailingWhiteSpace(string line)
    {
        int endIndex = line.Length;
        while (endIndex > 0 && char.IsWhiteSpace(line[endIndex - 1]))
        {
            int slashCount = 0;
            for (int i = endIndex - 2; i >= 0 && line[i] is '\\'; i--)
            {
                slashCount++;
            }

            if (slashCount.IsOdd())
            {
                break;
            }

            endIndex--;
        }

        return line[..endIndex];
    }
}
