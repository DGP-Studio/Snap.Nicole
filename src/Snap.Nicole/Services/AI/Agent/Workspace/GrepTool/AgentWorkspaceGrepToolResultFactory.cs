using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using Snap.Nicole.Core.IO;
using Snap.Nicole.Core.Text;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal static class AgentWorkspaceGrepToolResultFactory
{
    private const int BinaryProbeByteCount = 4096;
    private const string GitIgnoreFileName = ".gitignore";

    private static readonly EnumerationOptions DefaultEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false,
    };

    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(10);
    private static readonly FrozenSet<string> VersionControlDirectoryNames =
    [with(StringComparer.OrdinalIgnoreCase), ".git", ".svn", ".hg", ".bzr", ".jj", ".sl"];

    public static async Task<AIContent> CreateAsync(string callId, AgentWorkspacePath searchPath, AgentWorkspaceGrepToolOptions options, CancellationToken cancellationToken)
    {
        Regex regex = CreateRegex(options.Pattern, options);
        Matcher? globMatcher = CreateGlobMatcher(options.Glob);
        IReadOnlyList<GrepCandidate> candidates = CreateCandidates(searchPath, globMatcher, cancellationToken);
        IReadOnlyList<string> lines = await CreateGrepOutputLinesAsync(searchPath, candidates, regex, options, cancellationToken);
        GrepOutputWindow outputWindow = WindowGrepOutput(lines, options.Offset, options.HeadLimit);

        AgentWorkspaceGrepToolResult result = new()
        {
            SearchPath = searchPath.FullPath,
            OutputMode = options.OutputMode,
            Lines = [.. outputWindow.Lines],
            TotalLineCount = lines.Count,
            Offset = options.Offset,
            HeadLimit = options.HeadLimit,
            Truncated = outputWindow.Truncated,
        };

        AgentWorkspaceGrepToolResult.TryAdd(callId, result);
        return CreateTextContent(result);
    }

    private static TextContent CreateTextContent(AgentWorkspaceGrepToolResult result)
    {
        if (result.Lines.Count is 0)
        {
            return new(result.TotalLineCount is 0 ? "No matches found" : "No output lines after applying offset");
        }

        StringBuilder builder = new();
        for (int i = 0; i < result.Lines.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(result.Lines[i]);
        }

        if (result.Truncated)
        {
            builder.AppendLine();
            builder.Append("(Results are truncated. Consider using a more specific path or pattern.)");
        }

        return new(builder.ToString());
    }

    private static Regex CreateRegex(string pattern, AgentWorkspaceGrepToolOptions options)
    {
        RegexOptions regexOptions = RegexOptions.CultureInvariant;
        if (options.IgnoreCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        if (options.Multiline)
        {
            regexOptions |= RegexOptions.Multiline | RegexOptions.Singleline;
        }

        return new(pattern, regexOptions, RegexMatchTimeout);
    }

    private static Matcher? CreateGlobMatcher(string? glob)
    {
        if (string.IsNullOrWhiteSpace(glob))
        {
            return null;
        }

        string normalizedPattern = glob.Trim().Replace('\\', '/');
        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(normalizedPattern);
        if (!normalizedPattern.Contains('/', StringComparison.Ordinal))
        {
            matcher.AddInclude($"**/{normalizedPattern}");
        }

        return matcher;
    }

    private static IReadOnlyList<GrepCandidate> CreateCandidates(AgentWorkspacePath searchPath, Matcher? globMatcher, CancellationToken cancellationToken)
    {
        return searchPath switch
        {
            AgentWorkspaceFile searchFile => CreateCandidates(searchFile, globMatcher),
            AgentWorkspaceDirectory searchDirectory => CreateCandidates(searchDirectory, globMatcher, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported workspace search path type: {searchPath.GetType().FullName}."),
        };
    }

    private static IReadOnlyList<GrepCandidate> CreateCandidates(AgentWorkspaceFile searchFile, Matcher? globMatcher)
    {
        if (!File.Exists(searchFile.FullPath) || IsUnderVersionControlDirectory(searchFile))
        {
            return [];
        }

        GitIgnoreRuleSet ignoreRules = GitIgnoreRuleSet.CreateForDirectory(searchFile.RootDirectory, Path.GetDirectoryName(searchFile.FullPath) ?? searchFile.RootDirectory);
        GrepCandidate candidate = GrepCandidate.Create(searchFile, searchFile.FullPath);
        return !ignoreRules.IsIgnored(candidate.RootRelativePath, isDirectory: false) && CandidateMatchesFilters(candidate, globMatcher) ? [candidate] : [];
    }

    private static IReadOnlyList<GrepCandidate> CreateCandidates(AgentWorkspaceDirectory searchDirectory, Matcher? globMatcher, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(searchDirectory.FullPath) || IsUnderVersionControlDirectory(searchDirectory))
        {
            return [];
        }

        GitIgnoreRuleSet ancestorIgnoreRules = GitIgnoreRuleSet.CreateForParentDirectory(searchDirectory.RootDirectory, searchDirectory.FullPath);
        string searchDirectoryRootRelativePath = searchDirectory.GetRootRelativePath(searchDirectory.FullPath);
        if (!string.Equals(searchDirectoryRootRelativePath, ".", StringComparison.Ordinal) && ancestorIgnoreRules.IsIgnored(searchDirectoryRootRelativePath, isDirectory: true))
        {
            return [];
        }

        GitIgnoreRuleSet ignoreRules = ancestorIgnoreRules.CreateChild(searchDirectory.RootDirectory, searchDirectory.FullPath);
        List<GrepCandidate> candidates = [];
        CollectCandidates(searchDirectory, searchDirectory.FullPath, ignoreRules, globMatcher, candidates, cancellationToken);

        candidates.Sort(GrepCandidate.Comparer);
        return candidates;
    }

    private static void CollectCandidates(AgentWorkspaceDirectory searchDirectory, string directoryPath, GitIgnoreRuleSet ignoreRules, Matcher? globMatcher, List<GrepCandidate> candidates, CancellationToken cancellationToken)
    {
        foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*", DefaultEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath = Path.GetFullPath(filePath);
            GrepCandidate candidate = GrepCandidate.Create(searchDirectory, fullPath);
            if (!ignoreRules.IsIgnored(candidate.RootRelativePath, isDirectory: false) && CandidateMatchesFilters(candidate, globMatcher))
            {
                candidates.Add(candidate);
            }
        }

        foreach (string childDirectoryPath in Directory.EnumerateDirectories(directoryPath, "*", DefaultEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string childFullPath = Path.GetFullPath(childDirectoryPath);
            if (IsVersionControlDirectory(childFullPath))
            {
                continue;
            }

            string childRootRelativePath = searchDirectory.GetRootRelativePath(childFullPath);
            if (ignoreRules.IsIgnored(childRootRelativePath, isDirectory: true))
            {
                continue;
            }

            GitIgnoreRuleSet childIgnoreRules = ignoreRules.CreateChild(searchDirectory.RootDirectory, childFullPath);
            CollectCandidates(searchDirectory, childFullPath, childIgnoreRules, globMatcher, candidates, cancellationToken);
        }
    }

    private static bool CandidateMatchesFilters(GrepCandidate candidate, Matcher? globMatcher)
    {
        if (globMatcher is null)
        {
            return true;
        }

        return globMatcher.Match(candidate.RootRelativePath).HasMatches || globMatcher.Match(candidate.SearchRelativePath).HasMatches;
    }

    private static bool IsUnderVersionControlDirectory(AgentWorkspacePath searchPath)
    {
        if (IsVersionControlDirectory(searchPath.FullPath))
        {
            return true;
        }

        string rootRelativePath = searchPath.GetRootRelativePath(searchPath.FullPath);
        if (string.Equals(rootRelativePath, ".", StringComparison.Ordinal))
        {
            return false;
        }

        string[] segments = rootRelativePath.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (VersionControlDirectoryNames.Contains(segments[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVersionControlDirectory(string directoryPath)
    {
        string directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(directoryPath));
        return VersionControlDirectoryNames.Contains(directoryName);
    }

    private static async Task<IReadOnlyList<string>> CreateGrepOutputLinesAsync(AgentWorkspacePath searchPath, IReadOnlyList<GrepCandidate> candidates, Regex regex, AgentWorkspaceGrepToolOptions options, CancellationToken cancellationToken)
    {
        List<string> lines = [];
        foreach (GrepCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryReadTextFileAsync(candidate.FullPath, cancellationToken) is not { } content)
            {
                continue;
            }

            GrepFileMatch match = options.Multiline ? CreateMultilineMatch(content, regex) : CreateLineMatch(content, regex);
            if (match.MatchCount is 0)
            {
                continue;
            }

            AppendFileOutput(lines, searchPath, candidate, match, options);
        }

        return lines;
    }

    private static async Task<string?> TryReadTextFileAsync(string fullPath, CancellationToken cancellationToken)
    {
        using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length is 0)
        {
            return string.Empty;
        }

        byte[] probeBuffer = new byte[(int)Math.Min(BinaryProbeByteCount, stream.Length)];
        int probeByteCount = await stream.ReadAsync(probeBuffer, cancellationToken);
        if (ContainsNullByte(probeBuffer, probeByteCount))
        {
            return null;
        }

        stream.Position = 0;
        using StreamReader reader = new(stream, Encoding.UTF8WithoutBOM, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static bool ContainsNullByte(byte[] buffer, int byteCount)
    {
        for (int i = 0; i < byteCount; i++)
        {
            if (buffer[i] is 0)
            {
                return true;
            }
        }

        return false;
    }

    private static GrepFileMatch CreateLineMatch(string content, Regex regex)
    {
        List<GrepTextLine> lines = CreateTextLines(content);
        List<GrepLineMatch> matches = [];
        int matchCount = 0;
        foreach (GrepTextLine line in lines)
        {
            MatchCollection lineMatches = regex.Matches(line.Text);
            if (lineMatches.Count is 0)
            {
                continue;
            }

            List<string> matchedValues = [];
            foreach (Match match in lineMatches)
            {
                if (match.Success)
                {
                    matchCount++;
                    if (match.Length > 0)
                    {
                        matchedValues.Add(match.Value);
                    }
                }
            }

            matches.Add(new()
            {
                Line = line,
                MatchedValues = matchedValues,
            });
        }

        return new()
        {
            Lines = lines,
            Matches = matches,
            MatchCount = matchCount,
        };
    }

    private static GrepFileMatch CreateMultilineMatch(string content, Regex regex)
    {
        string normalizedContent = content.ReplaceLineEndings("\n");
        List<GrepTextLine> lines = CreateTextLines(normalizedContent);
        MatchCollection textMatches = regex.Matches(normalizedContent);
        if (textMatches.Count is 0)
        {
            return new()
            {
                Lines = lines,
                Matches = [],
                MatchCount = 0,
            };
        }

        Dictionary<int, GrepLineMatch> matchesByLineNumber = [];
        int matchCount = 0;
        foreach (Match match in textMatches)
        {
            if (!match.Success)
            {
                continue;
            }

            matchCount++;
            foreach (GrepTextLine line in lines)
            {
                if (!LineIntersectsMatch(line, match))
                {
                    continue;
                }

                if (!matchesByLineNumber.TryGetValue(line.LineNumber, out GrepLineMatch? lineMatch))
                {
                    lineMatch = new()
                    {
                        Line = line,
                        MatchedValues = [],
                    };
                    matchesByLineNumber.Add(line.LineNumber, lineMatch);
                }

                if (match.Length > 0)
                {
                    lineMatch.MatchedValues.Add(match.Value);
                }
            }
        }

        return new()
        {
            Lines = lines,
            Matches = [.. matchesByLineNumber.Values.OrderBy(static match => match.Line.LineNumber)],
            MatchCount = matchCount,
        };
    }

    private static bool LineIntersectsMatch(GrepTextLine line, Match match)
    {
        if (match.Length is 0)
        {
            return match.Index >= line.StartIndex && match.Index <= line.EndExclusive;
        }

        int matchStart = match.Index;
        int matchEnd = match.Index + match.Length;
        int lineEnd = Math.Max(line.EndExclusive, line.StartIndex + 1);
        return matchStart < lineEnd && matchEnd > line.StartIndex;
    }

    private static List<GrepTextLine> CreateTextLines(string content)
    {
        string normalizedContent = content.ReplaceLineEndings("\n");
        string[] splitLines = normalizedContent.Split('\n');
        int lineCount = splitLines.Length > 0 && splitLines[^1].Length is 0 ? splitLines.Length - 1 : splitLines.Length;
        List<GrepTextLine> lines = [];
        int currentIndex = 0;
        for (int i = 0; i < lineCount; i++)
        {
            string line = splitLines[i];
            lines.Add(new()
            {
                LineNumber = i + 1,
                Text = line,
                StartIndex = currentIndex,
                EndExclusive = currentIndex + line.Length,
            });

            currentIndex += line.Length + 1;
        }

        return lines;
    }

    private static void AppendFileOutput(List<string> output, AgentWorkspacePath searchPath, GrepCandidate candidate, GrepFileMatch match, AgentWorkspaceGrepToolOptions options)
    {
        if (options.OutputMode is AgentWorkspaceGrepToolOutputMode.FilesWithMatches)
        {
            output.Add(CreateDisplayPath(searchPath, candidate.RootRelativePath));
            return;
        }

        if (options.OutputMode is AgentWorkspaceGrepToolOutputMode.Count)
        {
            output.Add($"{CreateDisplayPath(searchPath, candidate.RootRelativePath)}:{match.MatchCount.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        if (options.OnlyMatching)
        {
            AppendOnlyMatchingOutput(output, searchPath, candidate, match, options);
            return;
        }

        AppendContentOutput(output, searchPath, candidate, match, options);
    }

    private static void AppendOnlyMatchingOutput(List<string> output, AgentWorkspacePath searchPath, GrepCandidate candidate, GrepFileMatch match, AgentWorkspaceGrepToolOptions options)
    {
        string displayPath = CreateDisplayPath(searchPath, candidate.RootRelativePath);
        foreach (GrepLineMatch lineMatch in match.Matches)
        {
            foreach (string matchedValue in lineMatch.MatchedValues)
            {
                if (matchedValue.Length is 0)
                {
                    continue;
                }

                output.Add(options.ShowLineNumbers ? $"{displayPath}:{lineMatch.Line.LineNumber.ToString(CultureInfo.InvariantCulture)}:{matchedValue}" : $"{displayPath}:{matchedValue}");
            }
        }
    }

    private static void AppendContentOutput(List<string> output, AgentWorkspacePath searchPath, GrepCandidate candidate, GrepFileMatch match, AgentWorkspaceGrepToolOptions options)
    {
        string displayPath = CreateDisplayPath(searchPath, candidate.RootRelativePath);
        int previousLineNumber = 0;
        HashSet<int> writtenLineNumbers = [];
        foreach (GrepLineMatch lineMatch in match.Matches)
        {
            int firstLineNumber = Math.Max(1, lineMatch.Line.LineNumber - options.BeforeContext);
            int lastLineNumber = Math.Min(match.Lines.Count, lineMatch.Line.LineNumber + options.AfterContext);
            if (previousLineNumber > 0 && firstLineNumber > previousLineNumber + 1)
            {
                output.Add("--");
            }

            for (int lineNumber = firstLineNumber; lineNumber <= lastLineNumber; lineNumber++)
            {
                if (!writtenLineNumbers.Add(lineNumber))
                {
                    continue;
                }

                GrepTextLine line = match.Lines[lineNumber - 1];
                bool isMatchLine = lineNumber == lineMatch.Line.LineNumber;
                output.Add(CreateContentOutputLine(displayPath, line, isMatchLine, options.ShowLineNumbers));
                previousLineNumber = lineNumber;
            }
        }
    }

    private static string CreateContentOutputLine(string displayPath, GrepTextLine line, bool isMatchLine, bool showLineNumbers)
    {
        if (showLineNumbers)
        {
            string separator = isMatchLine ? ":" : "-";
            return $"{displayPath}{separator}{line.LineNumber.ToString(CultureInfo.InvariantCulture)}{separator}{line.Text}";
        }

        return isMatchLine ? $"{displayPath}:{line.Text}" : $"{displayPath}-{line.Text}";
    }

    private static string CreateDisplayPath(AgentWorkspacePath searchPath, string rootRelativePath)
    {
        string relativeLine = rootRelativePath.StartsWith("./", StringComparison.Ordinal) ? rootRelativePath[2..] : rootRelativePath;
        return $"{searchPath.RootDirectory.Replace('\\', '/')}/{relativeLine}";
    }

    private static GrepOutputWindow WindowGrepOutput(IReadOnlyList<string> lines, int offset, int headLimit)
    {
        List<string> results = [];
        if (offset >= lines.Count)
        {
            return new()
            {
                Lines = results,
                Truncated = false,
            };
        }

        int exclusiveEnd = lines.Count;
        if (headLimit is not 0)
        {
            long requestedEnd = (long)offset + headLimit;
            exclusiveEnd = (int)Math.Min(lines.Count, requestedEnd);
        }

        for (int i = offset; i < exclusiveEnd; i++)
        {
            results.Add(lines[i]);
        }

        return new()
        {
            Lines = results,
            Truncated = exclusiveEnd < lines.Count,
        };
    }

    private sealed class GrepCandidate
    {
        public static Comparer<GrepCandidate> Comparer { get; } = Comparer<GrepCandidate>.Create(static (left, right) => string.Compare(left.RootRelativePath, right.RootRelativePath, StringComparison.OrdinalIgnoreCase));

        public required string FullPath { get; init; }

        public required string RootRelativePath { get; init; }

        public required string SearchRelativePath { get; init; }

        public static GrepCandidate Create(AgentWorkspacePath searchPath, string fullPath)
        {
            return new()
            {
                FullPath = fullPath,
                RootRelativePath = searchPath.GetRootRelativePath(fullPath),
                SearchRelativePath = searchPath.GetRelativePath(fullPath),
            };
        }
    }

    private sealed class GitIgnoreRule
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
            return new(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexMatchTimeout);
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

    private sealed class GitIgnoreRuleSet
    {
        public static GitIgnoreRuleSet Empty { get; } = new()
        {
            Rules = [],
        };

        public required IReadOnlyList<GitIgnoreRule> Rules { get; init; }

        public static GitIgnoreRuleSet CreateForDirectory(string rootDirectory, string directoryPath)
        {
            string normalizedRootDirectory = Path.TrimEndingDirectorySeparator(rootDirectory);
            string normalizedDirectoryPath = Path.TrimEndingDirectorySeparator(directoryPath);
            if (!Path.IsEqualOrSubdirectory(normalizedRootDirectory, normalizedDirectoryPath))
            {
                return Empty;
            }

            List<string> directories = [];
            string currentDirectory = normalizedDirectoryPath;
            while (true)
            {
                directories.Add(currentDirectory);
                if (Path.IsEqual(normalizedRootDirectory, currentDirectory))
                {
                    break;
                }

                string? parentDirectory = Path.GetDirectoryName(currentDirectory);
                if (string.IsNullOrEmpty(parentDirectory))
                {
                    break;
                }

                currentDirectory = Path.TrimEndingDirectorySeparator(parentDirectory);
            }

            GitIgnoreRuleSet ruleSet = Empty;
            for (int i = directories.Count - 1; i >= 0; i--)
            {
                ruleSet = ruleSet.CreateChild(normalizedRootDirectory, directories[i]);
            }

            return ruleSet;
        }

        public static GitIgnoreRuleSet CreateForParentDirectory(string rootDirectory, string directoryPath)
        {
            string normalizedDirectoryPath = Path.TrimEndingDirectorySeparator(directoryPath);
            string? parentDirectory = Path.GetDirectoryName(normalizedDirectoryPath);
            return string.IsNullOrEmpty(parentDirectory) ? Empty : CreateForDirectory(rootDirectory, parentDirectory);
        }

        public GitIgnoreRuleSet CreateChild(string rootDirectory, string directoryPath)
        {
            IReadOnlyList<GitIgnoreRule> localRules = CreateRules(rootDirectory, directoryPath);
            if (localRules.Count is 0)
            {
                return this;
            }

            List<GitIgnoreRule> rules = new(Rules.Count + localRules.Count);
            for (int i = 0; i < Rules.Count; i++)
            {
                rules.Add(Rules[i]);
            }

            for (int i = 0; i < localRules.Count; i++)
            {
                rules.Add(localRules[i]);
            }

            return new()
            {
                Rules = rules,
            };
        }

        public bool IsIgnored(string rootRelativePath, bool isDirectory)
        {
            bool ignored = false;
            for (int i = 0; i < Rules.Count; i++)
            {
                GitIgnoreRule rule = Rules[i];
                if (rule.Matches(rootRelativePath, isDirectory))
                {
                    ignored = !rule.IsNegated;
                }
            }

            return ignored;
        }

        private static IReadOnlyList<GitIgnoreRule> CreateRules(string rootDirectory, string directoryPath)
        {
            string gitIgnorePath = Path.Combine(directoryPath, GitIgnoreFileName);
            if (!File.Exists(gitIgnorePath))
            {
                return [];
            }

            string baseRelativeDirectory = AgentWorkspacePath.GetRelativePath(rootDirectory, directoryPath);
            if (string.Equals(baseRelativeDirectory, ".", StringComparison.Ordinal))
            {
                baseRelativeDirectory = string.Empty;
            }

            string[] lines = File.ReadAllLines(gitIgnorePath, Encoding.UTF8WithoutBOM);
            List<GitIgnoreRule> rules = [];
            for (int i = 0; i < lines.Length; i++)
            {
                string line = i is 0 ? lines[i].TrimStart('\uFEFF') : lines[i];
                if (GitIgnoreRule.Create(baseRelativeDirectory, line) is { } rule)
                {
                    rules.Add(rule);
                }
            }

            return rules;
        }
    }

    private sealed class GrepFileMatch
    {
        public required List<GrepTextLine> Lines { get; init; }

        public required List<GrepLineMatch> Matches { get; init; }

        public required int MatchCount { get; init; }
    }

    private sealed class GrepLineMatch
    {
        public required GrepTextLine Line { get; init; }

        public required List<string> MatchedValues { get; init; }
    }

    private sealed class GrepOutputWindow
    {
        public required IReadOnlyList<string> Lines { get; init; }

        public required bool Truncated { get; init; }
    }

    private sealed class GrepTextLine
    {
        public required int LineNumber { get; init; }

        public required string Text { get; init; }

        public required int StartIndex { get; init; }

        public required int EndExclusive { get; init; }
    }
}
