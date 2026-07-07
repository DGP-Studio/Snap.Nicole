using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

[JsonConverter(typeof(JsonStringEnumConverter<AgentWorkspaceStructuredPatchHunkLineKind>))]
internal enum AgentWorkspaceStructuredPatchHunkLineKind
{
    Context,
    Addition,
    Deletion,
}
