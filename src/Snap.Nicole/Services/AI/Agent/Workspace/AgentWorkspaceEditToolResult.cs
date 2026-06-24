using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceEditToolResult
{
    [Description("The file path that was edited")]
    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }

    [Description("The original string that was replaced")]
    [JsonPropertyName("oldString")]
    public required string OldString { get; init; }

    [Description("The new string that replaced it")]
    [JsonPropertyName("newString")]
    public required string NewString { get; init; }

    [Description("The original file contents before editing")]
    [JsonPropertyName("originalFile")]
    public required string OriginalFile { get; init; }

    [Description("Diff patch showing the changes")]
    [JsonPropertyName("structuredPatch")]
    public required List<AgentWorkspaceEditToolHunk> StructuredPatch { get; init; }

    [Description("Whether the user modified the proposed changes")]
    [JsonPropertyName("userNodified")]
    public required bool UserModified { get; init; }

    [Description("Whether all occurrences were replaced")]
    [JsonPropertyName("replaceAll")]
    public required bool ReplaceAll { get; init; }

    public AgentWorkspaceEditToolGitDiff? GitDiff { get; init; }
}
