using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;

[JsonConverter(typeof(JsonStringEnumConverter<AgentWorkspaceWriteToolResultType>))]
internal enum AgentWorkspaceWriteToolResultType
{
    [JsonStringEnumMemberName("create")]
    Create,

    [JsonStringEnumMemberName("update")]
    Update,
}
