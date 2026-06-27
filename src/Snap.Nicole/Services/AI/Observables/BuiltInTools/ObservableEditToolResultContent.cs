using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.EditTool;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Observables.BuiltInTools;

internal sealed partial class ObservableEditToolResultContent : ObservableFunctionResultContent
{
    public required AgentWorkspaceEditToolResult EditResult { get; init; }

    [JsonIgnore]
    public string ChangeSummary { get => $"+{EditResult.Additions} -{EditResult.Deletions}"; }

    [JsonIgnore]
    public string FilePath { get => EditResult.FilePath; }

    public static ObservableEditToolResultContent Create(FunctionResultContent functionResultContent, AgentWorkspaceEditToolResult editResult, JsonSerializerOptions jsonOptions)
    {
        return new()
        {
            CallId = functionResultContent.CallId,
            Result = SerializeResult(functionResultContent.Result, jsonOptions),
            EditResult = editResult,
        };
    }
}
