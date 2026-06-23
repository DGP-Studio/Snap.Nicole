using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceGrepTool(AgentWorkspaceContext context)
    : AgentWorkspaceToolComponent(context)
{
    private const string GrepContentOutputMode = "content";
    private const string GrepFilesWithMatchesOutputMode = "files_with_matches";
    private const string GrepCountOutputMode = "count";
    private const int DefaultGrepHeadLimit = 250;
    private const int BinaryProbeByteCount = 4096;

    private static readonly EnumerationOptions DefaultEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true,
    };

    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(10);

    private static readonly IReadOnlyDictionary<string, string[]> GrepFileTypeExtensions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["c"] = [".c", ".h"],
        ["cpp"] = [".cc", ".cpp", ".cxx", ".hpp", ".hxx", ".hh"],
        ["cs"] = [".cs"],
        ["csharp"] = [".cs"],
        ["css"] = [".css"],
        ["go"] = [".go"],
        ["html"] = [".htm", ".html"],
        ["java"] = [".java"],
        ["js"] = [".js", ".jsx", ".mjs", ".cjs"],
        ["json"] = [".json"],
        ["md"] = [".md", ".markdown"],
        ["markdown"] = [".md", ".markdown"],
        ["php"] = [".php"],
        ["py"] = [".py", ".pyw"],
        ["python"] = [".py", ".pyw"],
        ["rb"] = [".rb"],
        ["ruby"] = [".rb"],
        ["rs"] = [".rs"],
        ["rust"] = [".rs"],
        ["swift"] = [".swift"],
        ["ts"] = [".ts", ".tsx"],
        ["typescript"] = [".ts", ".tsx"],
        ["xml"] = [".xml", ".xaml", ".csproj", ".props", ".targets", ".resx", ".manifest"],
        ["yaml"] = [".yml", ".yaml"],
        ["yml"] = [".yml", ".yaml"],
    };

    public override AITool Tool { get => field ??= AIFunctionFactory.Create(GrepAsync); }

    [DisplayName(AgentWorkspaceToolNames.Grep)]
    [Description("""
        Content search implemented inside the workspace provider. Prefer this over shell grep commands - results integrate with the permission UI and file links.

        - Full .NET regular expression syntax (e.g. "log.*Error", "function\s+\w+").
        - Filter with `glob` (e.g. "**/*.tsx") or `type` (e.g. "js", "py", "rust").
        - `output_mode`: "content" (matching lines), "files_with_matches" (paths only, default), or "count".
        - `multiline: true` for patterns that span lines.
        """)]
    private async Task<List<string>> GrepAsync([Description("Absolute file or directory to search. Defaults to current working directory.")][DefaultValue(null)] string? path, [Description("The regular expression pattern to search for in file contents")] string pattern, [Description("Case insensitive search")][DefaultValue(false)] bool ignore_case, [Description("Enable multiline mode where . matches newlines and patterns can span lines. Default: false.")][DefaultValue(false)] bool multiline, [Description("""Show line numbers in output. Requires output_mode: "content", ignored otherwise. Defaults to true.""")][DefaultValue(true)] bool show_line_numbers, [Description("""Print only the matched (non-empty) parts of each matching line, one match per output line. Requires output_mode: "content", ignored otherwise. Defaults to false.""")][DefaultValue(false)] bool only_matching, [Description("""Number of lines to show before each match. Requires output_mode: "content", ignored otherwise.""")][DefaultValue(null)] int? before_context, [Description("""Number of lines to show after each match. Requires output_mode: "content", ignored otherwise.""")][DefaultValue(null)] int? after_context, [Description("""Number of lines to show before and after each match. Requires output_mode: "content", ignored otherwise.""")][DefaultValue(null)] int? context, [Description("""Glob pattern to filter files (e.g. "*.js", "*.{ts,tsx}")""")][DefaultValue(null)] string? glob, [Description("File type to search. Common types: js, py, rust, go, java, etc. More efficient than include for standard file types.")][DefaultValue(null)] string? type, [Description("""Output mode: "content" shows matching lines (supports context and line numbers), "files_with_matches" shows file paths, "count" shows match counts. Defaults to "files_with_matches".""")][DefaultValue("files_with_matches")] string? output_mode, [Description("""Limit output to first N lines/entries. Works across all output modes. Defaults to 250 when unspecified. Pass 0 for unlimited (use sparingly - large result sets waste context).""")][DefaultValue(250)] int? head_limit, [Description("""Skip first N lines/entries before applying head_limit. Works across all output modes. Defaults to 0.""")][DefaultValue(0)] int offset, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("Search pattern cannot be empty.", nameof(pattern));
        }

        GrepSearchOptions options = new()
        {
            OutputMode = NormalizeGrepOutputMode(output_mode),
            Offset = NormalizeNonNegativeOption(offset, nameof(offset), 0),
            HeadLimit = NormalizeNonNegativeOption(head_limit, nameof(head_limit), DefaultGrepHeadLimit),
            IgnoreCase = ignore_case,
            Multiline = multiline,
            ShowLineNumbers = show_line_numbers,
            OnlyMatching = only_matching,
            BeforeContext = NormalizeContextOption(context, before_context, nameof(context), nameof(before_context)),
            AfterContext = NormalizeContextOption(context, after_context, nameof(context), nameof(after_context)),
            GlobMatcher = CreateGlobMatcher(glob),
            TypeExtensions = ResolveTypeExtensions(type),
        };

        AgentWorkspacePath searchPath = Context.ResolveGrepSearchPath(path);
        Regex regex = CreateRegex(pattern, options);
        List<GrepCandidate> candidates = CreateCandidates(searchPath, options, cancellationToken);
        List<string> lines = await CreateGrepOutputLinesAsync(searchPath, candidates, regex, options, cancellationToken);
        return WindowGrepOutput(lines, options.Offset, options.HeadLimit);
    }

    private static Regex CreateRegex(string pattern, GrepSearchOptions options)
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
        if (!normalizedPattern.Contains("/", StringComparison.Ordinal))
        {
            matcher.AddInclude($"**/{normalizedPattern}");
        }

        return matcher;
    }

    private static string[]? ResolveTypeExtensions(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        string trimmedType = type.Trim();
        if (trimmedType.StartsWith(".", StringComparison.Ordinal))
        {
            return [trimmedType];
        }

        if (GrepFileTypeExtensions.TryGetValue(trimmedType, out string[]? extensions))
        {
            return extensions;
        }

        throw new ArgumentException($"Unsupported type: {type}.", nameof(type));
    }

    private static List<GrepCandidate> CreateCandidates(AgentWorkspacePath searchPath, GrepSearchOptions options, CancellationToken cancellationToken)
    {
        if (File.Exists(searchPath.FullPath))
        {
            GrepCandidate candidate = GrepCandidate.Create(searchPath, searchPath.FullPath);
            return CandidateMatchesFilters(candidate, options) ? [candidate] : [];
        }

        if (!Directory.Exists(searchPath.FullPath))
        {
            return [];
        }

        List<GrepCandidate> candidates = [];
        foreach (string filePath in Directory.EnumerateFiles(searchPath.FullPath, "*", DefaultEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath = Path.GetFullPath(filePath);
            GrepCandidate candidate = GrepCandidate.Create(searchPath, fullPath);
            if (CandidateMatchesFilters(candidate, options))
            {
                candidates.Add(candidate);
            }
        }

        candidates.Sort(GrepCandidate.Comparer);
        return candidates;
    }

    private static bool CandidateMatchesFilters(GrepCandidate candidate, GrepSearchOptions options)
    {
        if (options.TypeExtensions is not null && !ExtensionMatchesType(candidate.Extension, options.TypeExtensions))
        {
            return false;
        }

        if (options.GlobMatcher is null)
        {
            return true;
        }

        return options.GlobMatcher.Match(candidate.RootRelativePath).HasMatches || options.GlobMatcher.Match(candidate.SearchRelativePath).HasMatches;
    }

    private static bool ExtensionMatchesType(string extension, string[] typeExtensions)
    {
        foreach (string typeExtension in typeExtensions)
        {
            if (string.Equals(extension, typeExtension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<List<string>> CreateGrepOutputLinesAsync(AgentWorkspacePath searchPath, List<GrepCandidate> candidates, Regex regex, GrepSearchOptions options, CancellationToken cancellationToken)
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
        await using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
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

    private static void AppendFileOutput(List<string> output, AgentWorkspacePath searchPath, GrepCandidate candidate, GrepFileMatch match, GrepSearchOptions options)
    {
        if (string.Equals(options.OutputMode, GrepFilesWithMatchesOutputMode, StringComparison.Ordinal))
        {
            output.Add(CreateDisplayPath(searchPath, candidate.RootRelativePath));
            return;
        }

        if (string.Equals(options.OutputMode, GrepCountOutputMode, StringComparison.Ordinal))
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

    private static void AppendOnlyMatchingOutput(List<string> output, AgentWorkspacePath searchPath, GrepCandidate candidate, GrepFileMatch match, GrepSearchOptions options)
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

    private static void AppendContentOutput(List<string> output, AgentWorkspacePath searchPath, GrepCandidate candidate, GrepFileMatch match, GrepSearchOptions options)
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

    private static string NormalizeGrepOutputMode(string? outputMode)
    {
        if (string.IsNullOrWhiteSpace(outputMode))
        {
            return GrepFilesWithMatchesOutputMode;
        }

        string trimmedOutputMode = outputMode.Trim();
        if (string.Equals(trimmedOutputMode, GrepContentOutputMode, StringComparison.OrdinalIgnoreCase))
        {
            return GrepContentOutputMode;
        }

        if (string.Equals(trimmedOutputMode, GrepFilesWithMatchesOutputMode, StringComparison.OrdinalIgnoreCase))
        {
            return GrepFilesWithMatchesOutputMode;
        }

        if (string.Equals(trimmedOutputMode, GrepCountOutputMode, StringComparison.OrdinalIgnoreCase))
        {
            return GrepCountOutputMode;
        }

        throw new ArgumentException($"Unsupported output_mode: {outputMode}.", nameof(outputMode));
    }

    private static int NormalizeContextOption(int? contextValue, int? specificValue, string contextParameterName, string specificParameterName)
    {
        if (specificValue.HasValue)
        {
            return NormalizeNonNegativeOption(specificValue, specificParameterName, 0);
        }

        return NormalizeNonNegativeOption(contextValue, contextParameterName, 0);
    }

    private static int NormalizeNonNegativeOption(int? value, string parameterName, int defaultValue)
    {
        if (!value.HasValue)
        {
            return defaultValue;
        }

        if (value.Value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
        }

        return value.Value;
    }

    private static List<string> WindowGrepOutput(List<string> lines, int offset, int headLimit)
    {
        List<string> results = [];
        if (offset >= lines.Count)
        {
            return results;
        }

        int exclusiveEnd = headLimit is 0 ? lines.Count : Math.Min(lines.Count, offset + headLimit);
        for (int i = offset; i < exclusiveEnd; i++)
        {
            results.Add(lines[i]);
        }

        return results;
    }

    private sealed class GrepSearchOptions
    {
        public required string OutputMode { get; init; }

        public required int Offset { get; init; }

        public required int HeadLimit { get; init; }

        public required bool IgnoreCase { get; init; }

        public required bool Multiline { get; init; }

        public required bool ShowLineNumbers { get; init; }

        public required bool OnlyMatching { get; init; }

        public required int BeforeContext { get; init; }

        public required int AfterContext { get; init; }

        public required Matcher? GlobMatcher { get; init; }

        public required string[]? TypeExtensions { get; init; }
    }

    private sealed class GrepCandidate
    {
        public static Comparer<GrepCandidate> Comparer { get; } = Comparer<GrepCandidate>.Create(static (left, right) => string.Compare(left.RootRelativePath, right.RootRelativePath, StringComparison.OrdinalIgnoreCase));

        public required string FullPath { get; init; }

        public required string RootRelativePath { get; init; }

        public required string SearchRelativePath { get; init; }

        public required string Extension { get; init; }

        public static GrepCandidate Create(AgentWorkspacePath searchPath, string fullPath)
        {
            return new()
            {
                FullPath = fullPath,
                RootRelativePath = searchPath.GetRootRelativePath(fullPath),
                SearchRelativePath = searchPath.GetRelativePath(fullPath),
                Extension = Path.GetExtension(fullPath),
            };
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

    private sealed class GrepTextLine
    {
        public required int LineNumber { get; init; }

        public required string Text { get; init; }

        public required int StartIndex { get; init; }

        public required int EndExclusive { get; init; }
    }
}
