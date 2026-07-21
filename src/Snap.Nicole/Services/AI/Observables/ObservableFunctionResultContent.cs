using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.EditTool;
using Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;
using Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;
using Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;
using Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;
using Snap.Nicole.Services.AI.Observables.BuiltInTools;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Observables;

[JsonPolymorphic]
[JsonDerivedType(typeof(ObservableEditToolResultContent), "edit_tool_result")]
[JsonDerivedType(typeof(ObservableGlobToolResultContent), "glob_tool_result")]
[JsonDerivedType(typeof(ObservableGrepToolResultContent), "grep_tool_result")]
[JsonDerivedType(typeof(ObservableReadToolResultContent), "read_tool_result")]
[JsonDerivedType(typeof(ObservableWriteToolResultContent), "write_tool_result")]
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

        if (AgentWorkspaceGlobToolResult.TryRemove(functionResultContent.CallId, out AgentWorkspaceGlobToolResult? globResult))
        {
            return ObservableGlobToolResultContent.Create(functionResultContent, globResult, jsonOptions);
        }

        if (AgentWorkspaceGrepToolResult.TryRemove(functionResultContent.CallId, out AgentWorkspaceGrepToolResult? grepResult))
        {
            return ObservableGrepToolResultContent.Create(functionResultContent, grepResult, jsonOptions);
        }

        if (AgentWorkspaceReadToolResult.TryRemove(functionResultContent.CallId, out AgentWorkspaceReadToolResult? readResult))
        {
            return ObservableReadToolResultContent.Create(functionResultContent, readResult, jsonOptions);
        }

        if (AgentWorkspaceWriteToolResult.TryRemove(functionResultContent.CallId, out AgentWorkspaceWriteToolResult? writeResult))
        {
            return ObservableWriteToolResultContent.Create(functionResultContent, writeResult, jsonOptions);
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

        if (value is DataContent dataContent)
        {
            return FormatDataContent(dataContent);
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

    private static string FormatDataContent(DataContent dataContent)
    {
        string name = string.IsNullOrWhiteSpace(dataContent.Name) ? "content" : dataContent.Name;
        return $"{name} ({dataContent.MediaType}, {dataContent.Data.Length.ToString(CultureInfo.InvariantCulture)} bytes)";
    }
}
