using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

[JsonPolymorphic]
[JsonDerivedType(typeof(AgentWorkspaceReadToolTextResult), "text")]
[JsonDerivedType(typeof(AgentWorkspaceReadToolImageResult), "image")]
internal abstract class AgentWorkspaceReadToolResult;
