using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using Snap.Nicole.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

// See https://github.com/claude-code-best/claude-code/blob/main/packages/builtin-tools/src/tools/GlobTool/GlobTool.ts
internal sealed class AgentWorkspaceGlobTool(AgentWorkspaceContext context)
    : AgentWorkspaceTool(context)
{
    public override AITool Tool { get => field ??= AIFunctionFactory.Create(Glob); }

    [DisplayName(Prompt.GlobToolName)]
    [Description(Prompt.GlobToolDescription)]
    private AgentWorkspaceGlobToolResult Glob(
        [Description("The glob pattern to match files against")] string pattern,
        [Description("""The directory to search in. If not specified, the current working directory will be used. IMPORTANT: Omit this field to use the default directory. DO NOT enter "undefined" or "null" - simply omit it for the default behavior. Must be a valid directory path if provided.""")][DefaultValue(null)] string? path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern, "Glob pattern cannot be empty.", nameof(pattern));
        long startTimestamp = Stopwatch.GetTimestamp();

        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern.Replace('\\', '/'));

        AgentWorkspaceDirectory globDirectory = Context.ResolveGlobDirectoryPath(path);
        if (!Directory.Exists(globDirectory.FullPath))
        {
            return AgentWorkspaceGlobToolResult.Create([], Stopwatch.GetElapsedTime(startTimestamp));
        }

        List<GlobCandidate> candidates = CreateCandidates(globDirectory, cancellationToken);
        List<GlobResult> matches = CreateMatches(matcher, globDirectory.FullPath, candidates);
        return AgentWorkspaceGlobToolResult.Create([.. matches.Order(GlobResult.Comparer).Select(static result => result.FilePath)], Stopwatch.GetElapsedTime(startTimestamp));
    }

    private static List<GlobCandidate> CreateCandidates(AgentWorkspaceDirectory globDirectory, CancellationToken cancellationToken)
    {
        List<GlobCandidate> candidates = [];
        foreach (string filePath in Directory.EnumerateFiles(globDirectory.FullPath, "*", DefaultEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath = Path.GetFullPath(filePath);
            candidates.Add(new()
            {
                FilePath = fullPath,
                RelativePath = globDirectory.GetRelativePath(fullPath),
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath),
            });
        }

        return candidates;
    }

    private static List<GlobResult> CreateMatches(Matcher matcher, string rootDirectory, List<GlobCandidate> candidates)
    {
        Dictionary<string, GlobCandidate> candidatesByRelativePath = candidates
            .ToDictionary(static candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase);

        PatternMatchingResult matchResult = matcher.Match(rootDirectory, candidatesByRelativePath.Keys);
        if (!matchResult.HasMatches)
        {
            return [];
        }

        List<GlobResult> results = [];
        foreach (FilePatternMatch match in matchResult.Files)
        {
            GlobCandidate candidate = candidatesByRelativePath[match.Path];
            results.Add(GlobResult.Create(candidate));
        }

        return results;
    }

    private sealed class GlobCandidate
    {
        public required string FilePath { get; init; }

        public required string RelativePath { get; init; }

        public required DateTime LastWriteTimeUtc { get; init; }
    }

    private sealed class GlobResult
    {
        public static Comparer<GlobResult> Comparer { get; } = Comparer<GlobResult>.Create(static (left, right) =>
        {
            int result = right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
            return result is not 0 ? result : string.Compare(left.FilePath, right.FilePath, StringComparison.Ordinal);
        });

        public required string FilePath { get; init; }

        public required DateTime LastWriteTimeUtc { get; init; }

        public static GlobResult Create(GlobCandidate candidate)
        {
            return new()
            {
                FilePath = candidate.FilePath,
                LastWriteTimeUtc = candidate.LastWriteTimeUtc,
            };
        }
    }
}
