using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.EditTool;
using Snap.Nicole.Services.AI.Observables.BuiltInTools;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Observables;

[JsonPolymorphic]
[JsonDerivedType(typeof(ObservableEditToolResultContent), "edit_tool_result")]
internal partial class ObservableFunctionResultContent : ObservableToolResultContent
{
    [ObservableProperty]
    public partial string? Result { get; set; }

    public static ObservableFunctionResultContent Create(FunctionResultContent functionResultContent, JsonSerializerOptions jsonOptions)
    {
        if (AgentWorkspaceEditToolResult.TryRemove(functionResultContent.CallId, out AgentWorkspaceEditToolResult? editResult))
        {
            return ObservableEditToolResultContent.Create(functionResultContent, editResult, jsonOptions);
        }

        return new()
        {
            CallId = functionResultContent.CallId,
            Result = SerializeResult(functionResultContent.Result, jsonOptions),
        };
    }

    public void Update(ObservableFunctionResultContent functionResultContent)
    {
        Result = functionResultContent.Result;
    }

    protected static string? SerializeResult(object? value, JsonSerializerOptions jsonOptions)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.String } stringElement)
        {
            return stringElement.GetString();
        }

        try
        {
            return JsonSerializer.Serialize(value, jsonOptions);
        }
        catch
        {
            return value?.ToString();
        }
    }
}
