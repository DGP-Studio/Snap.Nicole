using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;

internal static class AgentWorkspaceGlobToolResultFactory
{
    private const string NoFilesFoundText = "No files found";
    private const string TruncatedText = "(Results are truncated. Consider using a more specific path or pattern.)";

    private static readonly EnumerationOptions DefaultEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true,
    };

    public static AIContent Create(AgentWorkspaceDirectory globDirectory, string pattern, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(globDirectory.FullPath))
        {
            return CreateTextContent([]);
        }

        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern.Replace('\\', '/'));

        List<GlobCandidate> candidates = CreateCandidates(globDirectory, cancellationToken);
        List<GlobResult> matches = CreateMatches(matcher, globDirectory.FullPath, candidates);
        return CreateTextContent([.. matches.Order(GlobResult.Comparer).Select(static result => result.FilePath)]);
    }

    private static TextContent CreateTextContent(IReadOnlyList<string> fileNames)
    {
        AgentWorkspaceGlobToolResult result = AgentWorkspaceGlobToolResult.Create(fileNames);
        if (result.FileNames.Count is 0)
        {
            return new TextContent(NoFilesFoundText);
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
            builder.Append(TruncatedText);
        }

        return new TextContent(builder.ToString());
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
