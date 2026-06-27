using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

[JsonConverter(typeof(JsonStringEnumConverter<AgentWorkspaceEditToolHunkLineKind>))]
internal enum AgentWorkspaceEditToolHunkLineKind
{
    Context,
    Addition,
    Deletion,
}
