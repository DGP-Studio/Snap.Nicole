using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using Snap.Nicole.Core.Collections.Generic;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;

internal static class AgentWorkspaceGlobToolResultFactory
{
    private const int MaximumGlobResultCount = 100;

    public static Task<AIContent> CreateAsync(string callId, AgentWorkspaceDirectory directory, string pattern, CancellationToken cancellationToken)
    {
        if (!directory.Exists)
        {
            return Task.FromResult<AIContent>(CreateTextContent(callId, []));
        }

        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern.Replace('\\', '/'));

        IReadOnlyList<GlobCandidate> matches = CreateGlobCandidates(directory, matcher, cancellationToken);
        return Task.FromResult<AIContent>(CreateTextContent(callId, [.. matches.Select(static result => result.RelativePath)]));
    }

    private static TextContent CreateTextContent(string callId, IReadOnlyList<string> relativePathList)
    {
        AgentWorkspaceGlobToolResult result = new()
        {
            FileNames = [.. relativePathList.Take(MaximumGlobResultCount)],
            Truncated = relativePathList.Count > MaximumGlobResultCount,
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

    private static IReadOnlyList<GlobCandidate> CreateGlobCandidates(AgentWorkspaceDirectory directory, Matcher matcher, CancellationToken cancellationToken)
    {
        Dictionary<string, GlobCandidate> candidatesByRelativePath = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (AgentWorkspaceFile file in directory.EnumerateFiles("*", recurseSubdirectories: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GlobCandidate candidate = GlobCandidate.Create(directory, file);
            candidatesByRelativePath.Add(candidate.RelativePath, candidate);
        }

        PatternMatchingResult matchResult = matcher.Match(directory.FullPath, candidatesByRelativePath.Keys);
        if (!matchResult.HasMatches)
        {
            return [];
        }

        ArrayBuilder<GlobCandidate> results = new();
        foreach (FilePatternMatch match in matchResult.Files)
        {
            results.Add(candidatesByRelativePath[match.Path]);
        }

        results.Sort(GlobCandidate.Comparer);
        return results.ToReadOnlyListAndClear();
    }

    private sealed class GlobCandidate
    {
        public static Comparer<GlobCandidate> Comparer { get; } = Comparer<GlobCandidate>.Create(static (left, right) =>
        {
            int result = right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
            return result is not 0 ? result : string.Compare(left.RelativePath, right.RelativePath, StringComparison.Ordinal);
        });

        public required string RelativePath { get; init; }

        public required DateTime LastWriteTimeUtc { get; init; }

        public static GlobCandidate Create(AgentWorkspaceDirectory directory, AgentWorkspaceFile file)
        {
            return new()
            {
                RelativePath = directory.GetRelativePath(file),
                LastWriteTimeUtc = file.LastWriteTimeUtc,
            };
        }
    }
}
