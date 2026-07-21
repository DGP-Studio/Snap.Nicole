using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

internal sealed class AgentWorkspaceStructuredPatchHunkLine
{
    [JsonPropertyName("kind")]
    public required AgentWorkspaceStructuredPatchHunkLineKind Kind { get; init; }

    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    public static AgentWorkspaceStructuredPatchHunkLine CreateAddition(int line, string text)
    {
        return new()
        {
            Kind = AgentWorkspaceStructuredPatchHunkLineKind.Addition,
            Line = line,
            Text = text,
        };
    }

    public static AgentWorkspaceStructuredPatchHunkLine CreateDeletion(int line, string text)
    {
        return new()
        {
            Kind = AgentWorkspaceStructuredPatchHunkLineKind.Deletion,
            Line = line,
            Text = text,
        };
    }

    public static AgentWorkspaceStructuredPatchHunkLine CreateContext(int line, string text)
    {
        return new()
        {
            Kind = AgentWorkspaceStructuredPatchHunkLineKind.Context,
            Line = line,
            Text = text,
        };
    }
}
