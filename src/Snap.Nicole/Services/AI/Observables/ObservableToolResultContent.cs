using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Nicole.Services.AI.Observables.BuiltInTools;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Observables;

[JsonPolymorphic]
[JsonDerivedType(typeof(ObservableCodeInterpreterToolResultContent), "code_interpreter_tool_result")]
[JsonDerivedType(typeof(ObservableEditToolResultContent), "edit_tool_result")]
[JsonDerivedType(typeof(ObservableFunctionResultContent), "function_result")]
[JsonDerivedType(typeof(ObservableGlobToolResultContent), "glob_tool_result")]
[JsonDerivedType(typeof(ObservableGrepToolResultContent), "grep_tool_result")]
[JsonDerivedType(typeof(ObservableImageGenerationToolResultContent), "image_generation_tool_result")]
[JsonDerivedType(typeof(ObservableMcpServerToolResultContent), "mcp_server_tool_result")]
[JsonDerivedType(typeof(ObservableReadToolResultContent), "read_tool_result")]
[JsonDerivedType(typeof(ObservableWebSearchToolResultContent), "web_search_tool_result")]
[JsonDerivedType(typeof(ObservableWriteToolResultContent), "write_tool_result")]
internal partial class ObservableToolResultContent : ObservableAIContent
{
    [ObservableProperty]
    public partial string CallId { get; set; }
}
