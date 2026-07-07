using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Observables.BuiltInTools;

internal sealed partial class ObservableReadToolResultContent : ObservableFunctionResultContent
{
    public required AgentWorkspaceReadToolResult ReadResult { get; init; }

    [JsonIgnore]
    public string FilePath { get => ReadResult.FilePath; }

    [JsonIgnore]
    public string Summary
    {
        get
        {
            return ReadResult switch
            {
                AgentWorkspaceReadToolTextResult textResult => CreateTextSummary(textResult),
                AgentWorkspaceReadToolImageResult imageResult => $"{imageResult.MediaType}, {imageResult.FileSize.ToString(CultureInfo.InvariantCulture)} bytes",
                _ => string.Empty,
            };
        }
    }

    public static ObservableReadToolResultContent Create(FunctionResultContent functionResultContent, AgentWorkspaceReadToolResult readResult, JsonSerializerOptions jsonOptions)
    {
        return new()
        {
            CallId = functionResultContent.CallId,
            Result = SerializeResult(functionResultContent.Result, jsonOptions),
            ReadResult = readResult,
        };
    }

    private static string CreateTextSummary(AgentWorkspaceReadToolTextResult result)
    {
        if (result.NumberOfLines is 0)
        {
            return $"0 lines of {result.TotalLines.ToString(CultureInfo.InvariantCulture)}";
        }

        int endLine = result.StartLine + result.NumberOfLines - 1;
        return $"Lines {result.StartLine.ToString(CultureInfo.InvariantCulture)}-{endLine.ToString(CultureInfo.InvariantCulture)} of {result.TotalLines.ToString(CultureInfo.InvariantCulture)}";
    }
}
