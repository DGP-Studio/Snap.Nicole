using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Observables.BuiltInTools;

internal sealed partial class ObservableGlobToolResultContent : ObservableFunctionResultContent
{
    public required AgentWorkspaceGlobToolResult GlobResult { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string> FileNames { get => GlobResult.FileNames; }

    [JsonIgnore]
    public string Summary { get => GlobResult.Truncated ? $"{GlobResult.FileNames.Count.ToString(CultureInfo.InvariantCulture)}+ files" : $"{GlobResult.FileNames.Count.ToString(CultureInfo.InvariantCulture)} files"; }

    public static ObservableGlobToolResultContent Create(FunctionResultContent functionResultContent, AgentWorkspaceGlobToolResult globResult, JsonSerializerOptions jsonOptions)
    {
        return new()
        {
            CallId = functionResultContent.CallId,
            Result = SerializeResult(functionResultContent.Result, jsonOptions),
            GlobResult = globResult,
        };
    }
}
