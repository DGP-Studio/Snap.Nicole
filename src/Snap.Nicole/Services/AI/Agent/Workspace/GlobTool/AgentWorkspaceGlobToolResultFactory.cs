using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;

internal static class AgentWorkspaceGlobToolResultFactory
{
    private const int MaximumGlobFileNameCount = 100;

    private static readonly EnumerationOptions DefaultEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true,
    };

    public static Task<AIContent> CreateAsync(string callId, AgentWorkspaceDirectory globDirectory, string pattern, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(globDirectory.FullPath))
        {
            return Task.FromResult<AIContent>(CreateTextContent(callId, []));
        }

        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern.Replace('\\', '/'));

        IReadOnlyList<GlobCandidate> candidates = CreateCandidates(globDirectory, cancellationToken);
        IReadOnlyList<GlobResult> matches = CreateMatches(matcher, globDirectory.FullPath, candidates);
        return Task.FromResult<AIContent>(CreateTextContent(callId, [.. matches.Order(GlobResult.Comparer).Select(static result => result.FilePath)]));
    }

    private static TextContent CreateTextContent(string callId, IReadOnlyList<string> fileNames)
    {
        AgentWorkspaceGlobToolResult result = new()
        {
            FileNames = [.. fileNames.Take(MaximumGlobFileNameCount)],
            Truncated = fileNames.Count > MaximumGlobFileNameCount,
        };

        AgentWorkspaceGlobToolResult.TryAdd(callId, result);

        if (result.FileNames.Count is 0)
        {
            return new TextContent("No files found");
        }

        StringBuilder builder = new();
        for (int i = 0; i < result.FileNames.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(result.FileNames[i]);
        }

        if (result.Truncated)
        {
            builder.AppendLine();
            builder.Append("(Results are truncated. Consider using a more specific path or pattern.)");
        }

        return new TextContent(builder.ToString());
    }

    private static IReadOnlyList<GlobCandidate> CreateCandidates(AgentWorkspaceDirectory globDirectory, CancellationToken cancellationToken)
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

    private static IReadOnlyList<GlobResult> CreateMatches(Matcher matcher, string rootDirectory, IReadOnlyList<GlobCandidate> candidates)
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
            results.Add(GlobResult.Create(candidatesByRelativePath[match.Path]));
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
