using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.EditTool;
using System.Text;
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

    [JsonIgnore]
    public string Patch { get => CreatePatch(EditResult); }

    public static ObservableEditToolResultContent Create(FunctionResultContent functionResultContent, AgentWorkspaceEditToolResult editResult, JsonSerializerOptions jsonOptions)
    {
        return new()
        {
            CallId = functionResultContent.CallId,
            Result = SerializeResult(functionResultContent.Result, jsonOptions),
            EditResult = editResult,
        };
    }

    private static string CreatePatch(AgentWorkspaceEditToolResult editResult)
    {
        StringBuilder builder = new();
        foreach (AgentWorkspaceEditToolHunk hunk in editResult.StructuredPatch)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append("@@ -");
            builder.Append(hunk.OldStart);
            builder.Append(',');
            builder.Append(hunk.OldLines);
            builder.Append(" +");
            builder.Append(hunk.NewStart);
            builder.Append(',');
            builder.Append(hunk.NewLines);
            builder.AppendLine(" @@");

            foreach (string line in hunk.Lines)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }
}
