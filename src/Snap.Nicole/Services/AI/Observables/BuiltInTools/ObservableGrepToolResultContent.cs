using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Observables.BuiltInTools;

internal sealed partial class ObservableGrepToolResultContent : ObservableFunctionResultContent
{
    public required AgentWorkspaceGrepToolResult GrepResult { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string> Lines { get => CreateLines(GrepResult); }

    [JsonIgnore]
    public string ResultLabel { get => CreateResultLabel(GrepResult); }

    [JsonIgnore]
    public string Summary { get => CreateSummary(GrepResult); }

    public static ObservableGrepToolResultContent Create(FunctionResultContent functionResultContent, AgentWorkspaceGrepToolResult grepResult, JsonSerializerOptions jsonOptions)
    {
        return new()
        {
            CallId = functionResultContent.CallId,
            Result = SerializeResult(functionResultContent.Result, jsonOptions),
            GrepResult = grepResult,
        };
    }

    private static string CreateSummary(AgentWorkspaceGrepToolResult result)
    {
        return result.Mode switch
        {
            AgentWorkspaceGrepToolOutputMode.Content => CreateContentSummary(result),
            AgentWorkspaceGrepToolOutputMode.Count => CreateCountSummary(result),
            _ => CreateFileSummary(result),
        };
    }

    private static IReadOnlyList<string> CreateLines(AgentWorkspaceGrepToolResult result)
    {
        if (!string.IsNullOrEmpty(result.Content))
        {
            return SplitLines(result.Content);
        }

        return result.FileNames;
    }

    private static string CreateResultLabel(AgentWorkspaceGrepToolResult result)
    {
        return result.Mode switch
        {
            AgentWorkspaceGrepToolOutputMode.Content => "content",
            AgentWorkspaceGrepToolOutputMode.Count => "count",
            AgentWorkspaceGrepToolOutputMode.FilesWithMatches => "files_with_matches",
            _ => string.Empty,
        };
    }

    private static string CreateContentSummary(AgentWorkspaceGrepToolResult result)
    {
        int visibleLineCountValue = CountVisibleLineCount(result);
        int totalLineCountValue = result.NumberOfLines ?? visibleLineCountValue;
        string visibleLineCount = visibleLineCountValue.ToString(CultureInfo.InvariantCulture);
        string totalLineCount = totalLineCountValue.ToString(CultureInfo.InvariantCulture);
        return visibleLineCountValue == totalLineCountValue ? $"{visibleLineCount} lines" : $"{visibleLineCount} of {totalLineCount} lines";
    }

    private static string CreateCountSummary(AgentWorkspaceGrepToolResult result)
    {
        int matchCountValue = result.NumberOfMatches ?? 0;
        string matchCount = matchCountValue.ToString(CultureInfo.InvariantCulture);
        return matchCountValue is 1 ? $"{matchCount} match" : $"{matchCount} matches";
    }

    private static string CreateFileSummary(AgentWorkspaceGrepToolResult result)
    {
        int visibleFileCountValue = result.FileNames.Count;
        string visibleFileCount = visibleFileCountValue.ToString(CultureInfo.InvariantCulture);
        string totalFileCount = result.NumberOfFiles.ToString(CultureInfo.InvariantCulture);
        return visibleFileCountValue == result.NumberOfFiles ? $"{visibleFileCount} files" : $"{visibleFileCount} of {totalFileCount} files";
    }

    private static int CountVisibleLineCount(AgentWorkspaceGrepToolResult result)
    {
        return string.IsNullOrEmpty(result.Content) ? 0 : SplitLines(result.Content).Count;
    }

    private static IReadOnlyList<string> SplitLines(string content)
    {
        string normalizedContent = content.ReplaceLineEndings("\n");
        string[] splitLines = normalizedContent.Split('\n');
        int lineCount = splitLines.Length > 0 && splitLines[^1].Length is 0 ? splitLines.Length - 1 : splitLines.Length;
        List<string> lines = new(lineCount);
        for (int i = 0; i < lineCount; i++)
        {
            lines.Add(splitLines[i]);
        }

        return lines;
    }
}
