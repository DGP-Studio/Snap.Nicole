using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Observables.BuiltInTools;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Observables;

[JsonPolymorphic]
[JsonDerivedType(typeof(ObservableCodeInterpreterToolCallContent), "code_interpreter_tool_call")]
[JsonDerivedType(typeof(ObservableCodeInterpreterToolResultContent), "code_interpreter_tool_result")]
[JsonDerivedType(typeof(ObservableDataContent), "data")]
[JsonDerivedType(typeof(ObservableEditToolResultContent), "edit_tool_result")]
[JsonDerivedType(typeof(ObservableErrorContent), "error")]
[JsonDerivedType(typeof(ObservableFunctionCallContent), "function_call")]
[JsonDerivedType(typeof(ObservableFunctionResultContent), "function_result")]
[JsonDerivedType(typeof(ObservableHostedFileContent), "hosted_file")]
[JsonDerivedType(typeof(ObservableHostedVectorStoreContent), "hosted_vector_store")]
[JsonDerivedType(typeof(ObservableImageGenerationToolCallContent), "image_generation_tool_call")]
[JsonDerivedType(typeof(ObservableImageGenerationToolResultContent), "image_generation_tool_result")]
[JsonDerivedType(typeof(ObservableInputRequestContent), "input_request")]
[JsonDerivedType(typeof(ObservableInputResponseContent), "input_response")]
[JsonDerivedType(typeof(ObservableMcpServerToolCallContent), "mcp_server_tool_call")]
[JsonDerivedType(typeof(ObservableMcpServerToolResultContent), "mcp_server_tool_result")]
[JsonDerivedType(typeof(ObservableTextContent), "text")]
[JsonDerivedType(typeof(ObservableTextReasoningContent), "text_reasoning")]
[JsonDerivedType(typeof(ObservableToolApprovalRequestContent), "tool_approval_request")]
[JsonDerivedType(typeof(ObservableToolApprovalResponseContent), "tool_approval_response")]
[JsonDerivedType(typeof(ObservableToolCallContent), "tool_call")]
[JsonDerivedType(typeof(ObservableToolResultContent), "tool_result")]
[JsonDerivedType(typeof(ObservableUriContent), "uri")]
[JsonDerivedType(typeof(ObservableUsageContent), "usage")]
[JsonDerivedType(typeof(ObservableWebSearchToolCallContent), "web_search_tool_call")]
[JsonDerivedType(typeof(ObservableWebSearchToolResultContent), "web_search_tool_result")]
internal class ObservableAIContent : ObservableObject
{
    public static ObservableAIContent? Create(AIContent content, JsonSerializerOptions jsonOptions)
    {
        return content switch
        {
            TextContent textContent when !string.IsNullOrEmpty(textContent.Text) => ObservableTextContent.Create(textContent),
            TextReasoningContent reasoningContent when !string.IsNullOrEmpty(reasoningContent.Text) => ObservableTextReasoningContent.Create(reasoningContent),
            FunctionCallContent functionCallContent => ObservableFunctionCallContent.Create(functionCallContent, jsonOptions),
            FunctionResultContent functionResultContent => ObservableFunctionResultContent.Create(functionResultContent, jsonOptions),
            ToolApprovalRequestContent toolApprovalRequestContent => ObservableToolApprovalRequestContent.Create(toolApprovalRequestContent, jsonOptions),
            ToolApprovalResponseContent toolApprovalResponseContent => ObservableToolApprovalResponseContent.Create(toolApprovalResponseContent),
            UsageContent usageContent => ObservableUsageContent.Create(usageContent),
            _ => null, // DO NOT throw here.
        };
    }
}
