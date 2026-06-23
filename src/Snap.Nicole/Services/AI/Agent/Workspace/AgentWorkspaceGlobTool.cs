using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using Snap.Nicole.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceGlobTool(AgentWorkspaceContext context)
    : AgentWorkspaceToolComponent(context)
{
    private static readonly EnumerationOptions DefaultEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true,
    };

    public override AITool Tool { get => field ??= AIFunctionFactory.Create(Glob); }

    [DisplayName(AgentWorkspaceToolNames.Glob)]
    [Description("""
        Fast file pattern matching. Supports glob patterns like "**/*.js" or "src/**/*.ts". Patterns are matched relative to the search directory. Returns matching file paths sorted by modification time.
        """)]
    private List<string> Glob(
        [Description("""The absolute directory to search in. If not specified, the current working directory will be used. IMPORTANT: Omit this field to use the default directory. DO NOT enter "undefined" or "null" - simply omit it for the default behavior.""")][DefaultValue(null)] string? path,
        [Description("The glob pattern to match files against")] string pattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern, "Glob pattern cannot be empty.", nameof(pattern));

        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern.Replace('\\', '/'));

        AgentWorkspacePath globDirectory = Context.ResolveGlobDirectoryPath(path);
        if (!Directory.Exists(globDirectory.FullPath))
        {
            return [];
        }

        List<GlobCandidate> candidates = CreateCandidates(globDirectory, cancellationToken);
        List<GlobResult> matches = CreateMatches(matcher, globDirectory.FullPath, candidates);
        return [.. matches.Order(GlobResult.Comparer).Select(static result => result.FilePath)];
    }

    private static List<GlobCandidate> CreateCandidates(AgentWorkspacePath globDirectory, CancellationToken cancellationToken)
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
