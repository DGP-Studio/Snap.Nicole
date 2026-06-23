using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceGlobToolResult
{
    private const int MaximumGlobFileNameCount = 100;

    [Description("Time taken to execute the search in milliseconds")]
    [JsonPropertyName("durationMs")]
    public required double DurationMilliseconds { get; init; }

    [Description("Total number of files found")]
    [JsonPropertyName("numFiles")]
    public required int NumberOfFiles { get; init; }

    [Description("Array of file paths that match the pattern")]
    [JsonPropertyName("filenames")]
    public required List<string> FileNames { get; init; }

    [Description("Whether results were truncated (limited to 100 files)")]
    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    public static AgentWorkspaceGlobToolResult Create(IReadOnlyList<string> fileNames, TimeSpan elapsedTimeSpan)
    {
        return new()
        {
            DurationMilliseconds = elapsedTimeSpan.TotalMilliseconds,
            NumberOfFiles = fileNames.Count,
            FileNames = [.. fileNames.Take(MaximumGlobFileNameCount)],
            Truncated = fileNames.Count > MaximumGlobFileNameCount,
        };
    }
}
