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
        string visibleLineCount = result.Lines.Count.ToString(CultureInfo.InvariantCulture);
        string totalLineCount = result.TotalLineCount.ToString(CultureInfo.InvariantCulture);
        return result.Lines.Count == result.TotalLineCount ? $"{visibleLineCount} lines" : $"{visibleLineCount} of {totalLineCount} lines";
    }
}
