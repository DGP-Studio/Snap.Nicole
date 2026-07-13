using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using Snap.Nicole.Core;
using Snap.Nicole.Core.Collections.Generic;
using Snap.Nicole.Core.Text;
using System.Buffers;
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

    private static readonly FrozenSet<string> VersionControlDirectoryNames = [with(StringComparer.OrdinalIgnoreCase), ".git", ".svn", ".hg", ".bzr", ".jj", ".sl"];

    public static async Task<AIContent> CreateAsync(string callId, AgentWorkspacePath searchPath, AgentWorkspaceGrepToolOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<GrepCandidate> candidates = CreateGrepCandidates(searchPath, CreateGlobMatcher(options.Glob), cancellationToken);
        GrepOutput output = await CreateGrepOutputAsync(candidates, CreateContentRegex(options.Pattern, options), options, cancellationToken);
        GrepOutputWindow outputWindow = WindowGrepOutput(output.Lines, options.Offset, options.HeadLimit);

        AgentWorkspaceGrepToolResult result = new()
        {
            Mode = options.OutputMode,
            NumberOfFiles = output.MatchedFileNames.Count,
            FileNames = [.. CreateResultFileNames(outputWindow, output, options)],
            Content = CreateResultContent(outputWindow.Lines, options.OutputMode),
            NumberOfLines = options.OutputMode is AgentWorkspaceGrepToolOutputMode.Content ? output.Lines.Count : null,
            NumberOfMatches = options.OutputMode is AgentWorkspaceGrepToolOutputMode.Count ? output.TotalOccurrenceCount : null,
            AppliedLimit = options.HeadLimit is 0 ? null : options.HeadLimit,
            AppliedOffset = options.Offset,
        };

        AgentWorkspaceGrepToolResult.TryAdd(callId, result);
        return CreateTextContent(outputWindow.Lines, output, options, outputWindow.Truncated);
    }

    private static TextContent CreateTextContent(IReadOnlyList<string> lines, GrepOutput output, AgentWorkspaceGrepToolOptions options, bool truncated)
    {
        if (lines.Count is 0)
        {
            return new(output.Lines.Count is 0 ? "No matches found" : "No output lines after applying offset");
        }

        IReadOnlyList<string> resultLines = CreateResultLines(lines, output, options, truncated);
        return new(JoinLines(resultLines));
    }

    private static IReadOnlyList<string> CreateResultFileNames(GrepOutputWindow outputWindow, GrepOutput output, AgentWorkspaceGrepToolOptions options)
    {
        return options.OutputMode is AgentWorkspaceGrepToolOutputMode.FilesWithMatches ? outputWindow.Lines : output.MatchedFileNames;
    }

    private static string? CreateResultContent(IReadOnlyList<string> lines, AgentWorkspaceGrepToolOutputMode outputMode)
    {
        return outputMode is not AgentWorkspaceGrepToolOutputMode.FilesWithMatches && lines.Count > 0 ? JoinLines(lines) : null;
    }

    private static string JoinLines(IReadOnlyList<string> lines)
    {
        StringBuilder builder = new();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(lines[i]);
        }

        return builder.ToString();
    }

    private static Regex CreateContentRegex(string pattern, AgentWorkspaceGrepToolOptions options)
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

        return new(pattern, regexOptions, TimeSpan.FromSeconds(10));
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

        // If the glob pattern does not contain a directory separator,
        // also include it as a pattern that matches any file in any subdirectory.
        if (!normalizedPattern.Contains('/', StringComparison.Ordinal))
        {
            matcher.AddInclude($"**/{normalizedPattern}");
        }

        return matcher;
    }

    private static IReadOnlyList<GrepCandidate> CreateGrepCandidates(AgentWorkspacePath searchPath, Matcher? globMatcher, CancellationToken cancellationToken)
    {
        return searchPath switch
        {
            AgentWorkspaceFile searchFile => CreateGrepCandidates(searchFile, globMatcher),
            AgentWorkspaceDirectory searchDirectory => CreateGrepCandidates(searchDirectory, globMatcher, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported workspace search path type: {TypeNameHelper.GetTypeDisplayName(searchPath)}."),
        };
    }

    private static IReadOnlyList<GrepCandidate> CreateGrepCandidates(AgentWorkspaceFile searchFile, Matcher? globMatcher)
    {
        if (!searchFile.Exists)
        {
            return [];
        }

        GrepCandidate candidate = GrepCandidate.Create(searchFile, searchFile);
        return candidate.GlobMatches(globMatcher) ? [candidate] : [];
    }

    private static IReadOnlyList<GrepCandidate> CreateGrepCandidates(AgentWorkspaceDirectory searchDirectory, Matcher? globMatcher, CancellationToken cancellationToken)
    {
        if (!searchDirectory.Exists)
        {
            return [];
        }

        GitIgnoreRuleSet ignoreRules = GitIgnoreRuleSet
            .CreateForParentDirectory(searchDirectory)
            .WithoutRulesMatching(searchDirectory.RootDirectory.GetRelativePath(searchDirectory.FullPath), isDirectory: true)
            .CreateChild(searchDirectory.RootDirectory, searchDirectory.FullPath);

        ArrayBuilder<GrepCandidate> candidates = new();
        CollectGrepCandidates(searchDirectory, searchDirectory, ignoreRules, globMatcher, candidates, cancellationToken);
        candidates.Sort(GrepCandidate.Comparer);

        return candidates.ToReadOnlyListAndClear();
    }

    private static void CollectGrepCandidates(AgentWorkspaceDirectory searchDirectory, AgentWorkspaceDirectory directory, GitIgnoreRuleSet ignoreRules, Matcher? globMatcher, ArrayBuilder<GrepCandidate> candidates, CancellationToken cancellationToken)
    {
        foreach (AgentWorkspaceFile file in directory.EnumerateFiles("*", recurseSubdirectories: false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            GrepCandidate candidate = GrepCandidate.Create(searchDirectory, file);
            if (ignoreRules.IsIgnored(candidate.RootRelativePath, false) || !candidate.GlobMatches(globMatcher))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        foreach (AgentWorkspaceDirectory childDirectory in directory.EnumerateDirectories("*", recurseSubdirectories: false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (VersionControlDirectoryNames.Contains(childDirectory.Name))
            {
                continue;
            }

            if (ignoreRules.IsIgnored(searchDirectory.RootDirectory.GetRelativePath(childDirectory.FullPath), true))
            {
                continue;
            }

            GitIgnoreRuleSet childIgnoreRules = ignoreRules.CreateChild(searchDirectory.RootDirectory, childDirectory.FullPath);
            CollectGrepCandidates(searchDirectory, childDirectory, childIgnoreRules, globMatcher, candidates, cancellationToken);
        }
    }

    private static async Task<GrepOutput> CreateGrepOutputAsync(IReadOnlyList<GrepCandidate> candidates, Regex contentRegex, AgentWorkspaceGrepToolOptions options, CancellationToken cancellationToken)
    {
        ArrayBuilder<string> lines = new();
        ArrayBuilder<string> matchedFileNames = new();
        int totalOccurrenceCount = 0;

        foreach (GrepCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await TryReadTextFileAsync(candidate.FullPath, cancellationToken) is not { } content)
            {
                continue;
            }

            Text normalizedContent = new(content);
            GrepFileMatch match = options.Multiline ? CreateMultilineMatch(normalizedContent, contentRegex) : CreateLineMatch(normalizedContent, contentRegex);
            if (match.MatchCount is 0)
            {
                continue;
            }

            totalOccurrenceCount += match.MatchCount;
            string displayPath = CreateDisplayPath(candidate);
            matchedFileNames.Add(displayPath);
            AppendFileOutput(lines, displayPath, match, options);
        }

        return new()
        {
            Lines = lines.ToReadOnlyListAndClear(),
            MatchedFileNames = matchedFileNames.ToReadOnlyListAndClear(),
            TotalOccurrenceCount = totalOccurrenceCount,
        };
    }

    private static async Task<string?> TryReadTextFileAsync(string fullPath, CancellationToken cancellationToken)
    {
        using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length is 0)
        {
            return string.Empty;
        }

        using (IMemoryOwner<byte> bufferOwner = MemoryPool<byte>.Shared.Rent((int)Math.Min(BinaryProbeByteCount, stream.Length)))
        {
            Memory<byte> buffer = bufferOwner.Memory;
            while (true)
            {
                int readCount = await stream.ReadAsync(buffer, cancellationToken);
                if (readCount is 0)
                {
                    break;
                }

                if (buffer.Span[..readCount].Contains((byte)0x00))
                {
                    // This file contains a null byte, which is a strong indicator that it is a binary file. Skip processing this file.
                    return null;
                }
            }
        }

        stream.Position = 0;
        using StreamReader reader = new(stream, Encoding.UTF8WithoutBOM, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static GrepFileMatch CreateLineMatch(Text content, Regex regex)
    {
        IReadOnlyList<GrepTextLine> lines = CreateTextLines(content);
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
                matchCount++;
                if (match.Length > 0)
                {
                    matchedValues.Add(match.Value);
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

    private static GrepFileMatch CreateMultilineMatch(Text content, Regex regex)
    {
        IReadOnlyList<GrepTextLine> lines = CreateTextLines(content);
        MatchCollection textMatches = regex.Matches(content.Value);
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
            return match.Index >= line.StartIndex && match.Index <= line.EndIndex;
        }

        int matchStart = match.Index;
        int matchEnd = match.Index + match.Length;
        int lineEnd = Math.Max(line.EndIndex, line.StartIndex + 1);
        return matchStart < lineEnd && matchEnd > line.StartIndex;
    }

    private static IReadOnlyList<GrepTextLine> CreateTextLines(Text content)
    {
        TextLineCollection textLines = TextLineCollection.FromText(content);
        ArrayBuilder<GrepTextLine> lines = new(textLines.Count);

        int currentIndex = 0;
        for (int i = 0; i < textLines.Count; i++)
        {
            string line = textLines[i];
            lines.Add(new()
            {
                LineNumber = i + 1,
                Text = line,
                StartIndex = currentIndex,
                EndIndex = currentIndex + line.Length,
            });

            // '\n' separates lines in the content.
            currentIndex += line.Length + 1;
        }

        return lines.ToReadOnlyListAndClear();
    }

    private static void AppendFileOutput(ArrayBuilder<string> output, string displayPath, GrepFileMatch match, AgentWorkspaceGrepToolOptions options)
    {
        if (options.OutputMode is AgentWorkspaceGrepToolOutputMode.FilesWithMatches)
        {
            output.Add(displayPath);
            return;
        }

        if (options.OutputMode is AgentWorkspaceGrepToolOutputMode.Count)
        {
            output.Add($"{displayPath}:{match.MatchCount.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        if (options.OnlyMatching)
        {
            AppendOnlyMatchingOutput(output, displayPath, match, options);
            return;
        }

        AppendContentOutput(output, displayPath, match, options);
    }

    private static void AppendOnlyMatchingOutput(ArrayBuilder<string> output, string displayPath, GrepFileMatch match, AgentWorkspaceGrepToolOptions options)
    {
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

    private static void AppendContentOutput(ArrayBuilder<string> output, string displayPath, GrepFileMatch match, AgentWorkspaceGrepToolOptions options)
    {
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

    private static IReadOnlyList<string> CreateResultLines(IReadOnlyList<string> lines, GrepOutput output, AgentWorkspaceGrepToolOptions options, bool truncated)
    {
        if (lines.Count is 0)
        {
            return lines;
        }

        List<string> resultLines = new(lines.Count + 4);
        for (int i = 0; i < lines.Count; i++)
        {
            resultLines.Add(lines[i]);
        }

        if (options.OutputMode is AgentWorkspaceGrepToolOutputMode.Count)
        {
            resultLines.Add(string.Empty);
            resultLines.Add(CreateCountSummary(output.TotalOccurrenceCount, output.MatchedFileNames.Count));
        }

        if (truncated)
        {
            resultLines.Add(string.Empty);
            resultLines.Add(CreatePaginationSummary(options.HeadLimit));
        }

        return resultLines;
    }

    private static string CreateCountSummary(int totalOccurrenceCount, int matchedFileCount)
    {
        string occurrenceName = totalOccurrenceCount is 1 ? "occurrence" : "occurrences";
        string fileName = matchedFileCount is 1 ? "file" : "files";
        return $"Found {totalOccurrenceCount.ToString(CultureInfo.InvariantCulture)} total {occurrenceName} across {matchedFileCount.ToString(CultureInfo.InvariantCulture)} {fileName}.";
    }

    private static string CreatePaginationSummary(int headLimit)
    {
        return $"[Showing results with pagination = limit: {headLimit.ToString(CultureInfo.InvariantCulture)}]";
    }

    private static string CreateDisplayPath(GrepCandidate candidate)
    {
        string displayPath = candidate.SearchRelativePath;
        if (string.Equals(displayPath, ".", StringComparison.Ordinal) || displayPath.Length is 0)
        {
            displayPath = Path.GetFileName(candidate.FullPath);
        }

        return displayPath.StartsWith("./", StringComparison.Ordinal) ? displayPath[2..] : displayPath;
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

        public static GrepCandidate Create(AgentWorkspacePath searchPath, AgentWorkspaceFile file)
        {
            return new()
            {
                FullPath = file.FullPath,
                RootRelativePath = searchPath.RootDirectory.GetRelativePath(file.FullPath),
                SearchRelativePath = searchPath.GetRelativePath(file.FullPath),
            };
        }

        public bool GlobMatches(Matcher? globMatcher)
        {
            return globMatcher is null || globMatcher.Match(SearchRelativePath).HasMatches;
        }
    }

    private sealed class GrepFileMatch
    {
        public required IReadOnlyList<GrepTextLine> Lines { get; init; }

        public required List<GrepLineMatch> Matches { get; init; }

        public required int MatchCount { get; init; }
    }

    private sealed class GrepOutput
    {
        public required IReadOnlyList<string> Lines { get; init; }

        public required IReadOnlyList<string> MatchedFileNames { get; init; }

        public required int TotalOccurrenceCount { get; init; }
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

        public required int EndIndex { get; init; }
    }
}
