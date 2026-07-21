using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

[JsonConverter(typeof(JsonStringEnumConverter<AgentWorkspaceGrepToolOutputMode>))]
internal enum AgentWorkspaceGrepToolOutputMode
{
    [JsonStringEnumMemberName("content")]
    Content,

    [JsonStringEnumMemberName("files_with_matches")]
    FilesWithMatches,

    [JsonStringEnumMemberName("count")]
    Count,
}
