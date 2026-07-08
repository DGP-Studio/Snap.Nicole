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
    public IReadOnlyList<string> Lines { get => GrepResult.Lines; }

    [JsonIgnore]
    public string SearchPath { get => GrepResult.SearchPath; }

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
        int visibleLineCountValue = CountVisibleLineCount(result);
        string visibleLineCount = visibleLineCountValue.ToString(CultureInfo.InvariantCulture);
        string totalLineCount = result.TotalLineCount.ToString(CultureInfo.InvariantCulture);
        return visibleLineCountValue == result.TotalLineCount ? $"{visibleLineCount} lines" : $"{visibleLineCount} of {totalLineCount} lines";
    }

    private static int CountVisibleLineCount(AgentWorkspaceGrepToolResult result)
    {
        if (result.Lines.Count is 0)
        {
            return 0;
        }

        int lineCount = result.Lines.Count;
        if (result.OutputMode is AgentWorkspaceGrepToolOutputMode.Count)
        {
            lineCount -= 2;
        }

        if (result.Truncated)
        {
            lineCount -= 2;
        }

        return Math.Max(0, lineCount);
    }
}
