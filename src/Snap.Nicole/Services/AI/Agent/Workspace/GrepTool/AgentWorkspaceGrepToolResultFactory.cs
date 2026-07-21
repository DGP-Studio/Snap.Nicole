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
using System.Runtime.InteropServices;
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
            FileNames = options.OutputMode is AgentWorkspaceGrepToolOutputMode.FilesWithMatches ? outputWindow.Lines : output.MatchedFileNames,
            Content = options.OutputMode is not AgentWorkspaceGrepToolOutputMode.FilesWithMatches && outputWindow.Lines.Count > 0 ? string.Join(Environment.NewLine, outputWindow.Lines) : null,
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

        return new(CreateResultText(lines, output, options, truncated));
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
            GrepFileMatch fileMatch = options.Multiline
                ? CreateMultilineMatch(normalizedContent, contentRegex)
                : CreateLineMatch(normalizedContent, contentRegex);

            if (fileMatch.MatchCount is 0)
            {
                continue;
            }

            totalOccurrenceCount += fileMatch.MatchCount;
            string displayPath = candidate.DisplayPath;
            matchedFileNames.Add(displayPath);
            AppendFileOutput(lines, displayPath, fileMatch, options);
        }

        return GrepOutput.Create(lines, matchedFileNames, totalOccurrenceCount);
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
        TextLineCollection lines = TextLineCollection.FromText(content, trimTrailingEmptyLine: true);
        ArrayBuilder<GrepLineMatch> matches = new();
        int matchCount = 0;

        foreach (TextLine line in lines)
        {
            MatchCollection lineMatches = regex.Matches(line.ToString());
            if (lineMatches.Count is 0)
            {
                continue;
            }

            ArrayBuilder<TextSpan> matchedSpans = new();
            foreach (Match match in lineMatches)
            {
                matchCount++;
                if (match.Length > 0)
                {
                    matchedSpans.Add(new(line.Start + match.Index, match.Length));
                }
            }

            matches.Add(GrepLineMatch.Create(line, matchedSpans));
        }

        return GrepFileMatch.Create(lines, matches, matchCount);
    }

    private static GrepFileMatch CreateMultilineMatch(Text content, Regex regex)
    {
        TextLineCollection lines = TextLineCollection.FromText(content, trimTrailingEmptyLine: true);
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

        Dictionary<int, GrepLineMatchBuilder> matchBuildersByLineNumber = [];
        int matchCount = 0;
        foreach (Match match in textMatches)
        {
            matchCount++;
            TextSpan matchSpan = new(match.Index, match.Length);
            foreach (TextLine line in lines)
            {
                if (!LineOverlapsWithMatch(line, matchSpan))
                {
                    continue;
                }

                ref GrepLineMatchBuilder? lineMatchBuilder = ref CollectionsMarshal.GetValueRefOrAddDefault(matchBuildersByLineNumber, line.LineNumber, out _);
                lineMatchBuilder ??= GrepLineMatchBuilder.Create(line);

                if (!matchSpan.IsEmpty)
                {
                    lineMatchBuilder.MatchedSpans.Add(matchSpan);
                }
            }
        }

        ArrayBuilder<GrepLineMatch> matches = new(matchBuildersByLineNumber.Count);
        foreach (GrepLineMatchBuilder lineMatchBuilder in matchBuildersByLineNumber.Values.OrderBy(static match => match.Line.LineNumber))
        {
            matches.Add(lineMatchBuilder.ToGrepLineMatch());
        }

        return new()
        {
            Lines = lines,
            Matches = matches.ToReadOnlyListAndClear(),
            MatchCount = matchCount,
        };
    }

    private static bool LineOverlapsWithMatch(TextLine line, TextSpan matchSpan)
    {
        // This is a special version of the TextSpan.OverlapsWith method that allows for zero-length matches
        // to be considered overlapping with a line if the fileMatch position is within the line's span.
        // And it also allows for matches that start at the end of a line to be considered overlapping with that line.
        if (matchSpan.IsEmpty)
        {
            return line.Span.Contains(matchSpan);
        }

        return matchSpan.Start < Math.Max(line.End, line.Start + 1) && matchSpan.End > line.Start;
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
            output.Add($"{displayPath}:{match.MatchCount}");
            return;
        }

        // Mode: content
        if (options.OnlyMatching)
        {
            foreach (GrepLineMatch lineMatch in match.Matches)
            {
                foreach (TextSpan matchedSpan in lineMatch.MatchedSpans)
                {
                    if (matchedSpan.IsEmpty)
                    {
                        continue;
                    }

                    ReadOnlySpan<char> matchedValue = lineMatch.Line.Text.ToString(matchedSpan);
                    output.Add(options.ShowLineNumbers
                        ? $"{displayPath}:{lineMatch.Line.LineNumber}:{matchedValue}"
                        : $"{displayPath}:{matchedValue}");
                }
            }

            return;
        }

        int previousLineNumber = -1;
        HashSet<int> writtenLineNumbers = [];
        foreach (GrepLineMatch lineMatch in match.Matches)
        {
            int firstLineNumber = Math.Max(0, lineMatch.Line.LineNumber - options.BeforeContext);
            int lastLineNumber = Math.Min(match.Lines.Count - 1, lineMatch.Line.LineNumber + options.AfterContext);
            if (previousLineNumber >= 0 && firstLineNumber > previousLineNumber + 1)
            {
                // Block separator between non-contiguous match blocks
                output.Add("--");
            }

            for (int lineNumber = firstLineNumber; lineNumber <= lastLineNumber; lineNumber++)
            {
                if (!writtenLineNumbers.Add(lineNumber))
                {
                    continue;
                }

                TextLine line = match.Lines[lineNumber];
                bool isMatchLine = lineNumber == lineMatch.Line.LineNumber;
                string separator = isMatchLine ? ":" : "-";
                output.Add(options.ShowLineNumbers
                    ? $"{displayPath}{separator}{line.LineNumber}{separator}{line}"
                    : $"{displayPath}{separator}{line}");
                previousLineNumber = lineNumber;
            }
        }
    }

    private static string CreateResultText(IReadOnlyList<string> lines, GrepOutput output, AgentWorkspaceGrepToolOptions options, bool truncated)
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

        if (options.OutputMode is AgentWorkspaceGrepToolOutputMode.Count)
        {
            string occurrenceName = output.TotalOccurrenceCount is 1 ? "occurrence" : "occurrences";
            string fileName = output.MatchedFileNames.Count is 1 ? "file" : "files";
            builder.AppendLine();
            builder.AppendLine();
            builder.Append($"Found {output.TotalOccurrenceCount} total {occurrenceName} across {output.MatchedFileNames.Count} {fileName}.");
        }

        if (truncated)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append($"[Showing results with pagination = limit: {options.HeadLimit}]");
        }

        return builder.ToString();
    }

    private static GrepOutputWindow WindowGrepOutput(IReadOnlyList<string> lines, int offset, int headLimit)
    {
        if (offset >= lines.Count)
        {
            return GrepOutputWindow.Empty;
        }

        List<string> results = [];
        int exclusiveEnd = lines.Count;
        if (headLimit is not 0)
        {
            // Grep results count are unlikely to reach int.MaxValue
            // so we can safely use Math.Min here without worrying about overflow
            exclusiveEnd = Math.Min(lines.Count, offset + headLimit);
        }

        for (int i = offset; i < exclusiveEnd; i++)
        {
            results.Add(lines[i]);
        }

        return GrepOutputWindow.Create(results, exclusiveEnd < lines.Count);
    }

    private sealed class GrepCandidate
    {
        public static Comparer<GrepCandidate> Comparer { get; } = Comparer<GrepCandidate>.Create(static (left, right) => string.Compare(left.RootRelativePath, right.RootRelativePath, StringComparison.OrdinalIgnoreCase));

        public required string DisplayPath { get; init; }

        public required string FullPath { get; init; }

        public required string RootRelativePath { get; init; }

        public required string SearchRelativePath { get; init; }

        public static GrepCandidate Create(AgentWorkspacePath searchPath, AgentWorkspaceFile file)
        {
            string searchRelativePath = searchPath.GetRelativePath(file.FullPath);
            string displayPath = searchRelativePath;
            if (string.Equals(displayPath, ".", StringComparison.Ordinal) || displayPath.Length is 0)
            {
                displayPath = Path.GetFileName(file.FullPath);
            }

            return new()
            {
                DisplayPath = displayPath.StartsWith("./", StringComparison.Ordinal) ? displayPath[2..] : displayPath,
                FullPath = file.FullPath,
                RootRelativePath = searchPath.RootDirectory.GetRelativePath(file.FullPath),
                SearchRelativePath = searchRelativePath,
            };
        }

        public bool GlobMatches(Matcher? globMatcher)
        {
            return globMatcher is null || globMatcher.Match(SearchRelativePath).HasMatches;
        }
    }

    private sealed class GrepOutput
    {
        public required IReadOnlyList<string> Lines { get; init; }

        public required IReadOnlyList<string> MatchedFileNames { get; init; }

        public required int TotalOccurrenceCount { get; init; }

        public static GrepOutput Create(ArrayBuilder<string> lines, ArrayBuilder<string> matchedFileNames, int totalOccurrenceCount)
        {
            return new()
            {
                Lines = lines.ToReadOnlyListAndClear(),
                MatchedFileNames = matchedFileNames.ToReadOnlyListAndClear(),
                TotalOccurrenceCount = totalOccurrenceCount,
            };
        }
    }

    private sealed class GrepFileMatch
    {
        public required TextLineCollection Lines { get; init; }

        public required IReadOnlyList<GrepLineMatch> Matches { get; init; }

        public required int MatchCount { get; init; }

        public static GrepFileMatch Create(TextLineCollection lines, ArrayBuilder<GrepLineMatch> matches, int matchCount)
        {
            return new()
            {
                Lines = lines,
                Matches = matches.ToReadOnlyListAndClear(),
                MatchCount = matchCount,
            };
        }
    }

    private sealed class GrepLineMatch
    {
        public required TextLine Line { get; init; }

        public required IReadOnlyList<TextSpan> MatchedSpans { get; init; }

        public static GrepLineMatch Create(TextLine line, ArrayBuilder<TextSpan> matchedSpans)
        {
            return new()
            {
                Line = line,
                MatchedSpans = matchedSpans.ToReadOnlyListAndClear(),
            };
        }
    }

    private sealed class GrepLineMatchBuilder
    {
        public required TextLine Line { get; init; }

        public required ArrayBuilder<TextSpan> MatchedSpans { get; init; }

        public static GrepLineMatchBuilder Create(TextLine line)
        {
            return new()
            {
                Line = line,
                MatchedSpans = new(),
            };
        }

        public GrepLineMatch ToGrepLineMatch()
        {
            return GrepLineMatch.Create(Line, MatchedSpans);
        }
    }

    private sealed class GrepOutputWindow
    {
        public static GrepOutputWindow Empty { get; } = Create([], false);

        public required IReadOnlyList<string> Lines { get; init; }

        public required bool Truncated { get; init; }

        public static GrepOutputWindow Create(IReadOnlyList<string> lines, bool truncated)
        {
            return new()
            {
                Lines = lines,
                Truncated = truncated,
            };
        }
    }
}
