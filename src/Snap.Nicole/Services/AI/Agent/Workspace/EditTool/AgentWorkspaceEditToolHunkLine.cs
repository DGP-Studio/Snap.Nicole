using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal sealed class AgentWorkspaceEditToolHunkLine
{
    [JsonPropertyName("kind")]
    public required AgentWorkspaceEditToolHunkLineKind Kind { get; init; }

    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    public static AgentWorkspaceEditToolHunkLine CreateAddition(int line, string text)
    {
        return new()
        {
            Kind = AgentWorkspaceEditToolHunkLineKind.Addition,
            Line = line,
            Text = text,
        };
    }

    public static AgentWorkspaceEditToolHunkLine CreateDeletion(int line, string text)
    {
        return new()
        {
            Kind = AgentWorkspaceEditToolHunkLineKind.Deletion,
            Line = line,
            Text = text,
        };
    }

    public static AgentWorkspaceEditToolHunkLine CreateContext(int line, string text)
    {
        return new()
        {
            Kind = AgentWorkspaceEditToolHunkLineKind.Context,
            Line = line,
            Text = text,
        };
    }
}
