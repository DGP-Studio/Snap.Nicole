using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Observables.BuiltInTools;

internal sealed partial class ObservableWriteToolResultContent : ObservableFunctionResultContent
{
    public required AgentWorkspaceWriteToolResult WriteResult { get; init; }

    [JsonIgnore]
    public string FilePath { get => WriteResult.FilePath; }

    [JsonIgnore]
    public string ChangeSummary { get => WriteResult.Type is AgentWorkspaceWriteToolResultType.Create ? $"+{WriteResult.Additions}" : $"+{WriteResult.Additions} -{WriteResult.Deletions}"; }

    public static ObservableWriteToolResultContent Create(FunctionResultContent functionResultContent, AgentWorkspaceWriteToolResult writeResult, JsonSerializerOptions jsonOptions)
    {
        return new()
        {
            CallId = functionResultContent.CallId,
            Result = SerializeResult(functionResultContent.Result, jsonOptions),
            WriteResult = writeResult,
        };
    }
}
