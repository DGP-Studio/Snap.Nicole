using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal sealed class AgentWorkspaceEditToolGitDiff
{
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("status")]
    public required AgentWorkspaceEditToolGitDiffStatus Status { get; init; }

    [JsonPropertyName("additions")]
    public required int Additions { get; init; }

    [JsonPropertyName("deletions")]
    public required int Deletions { get; init; }

    [JsonPropertyName("changes")]
    public required int Changes { get; init; }

    [JsonPropertyName("patch")]
    public required string Patch { get; init; }

    [Description("GitHub owner/repo when available")]
    [JsonPropertyName("repository")]
    public string? Repository { get; init; }
}
