using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

namespace Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;

internal static class AgentWorkspaceWriteToolResultFactory
{
    public static AIContent Create(string callId, AgentWorkspaceFile file, string content, string? originalFile)
    {
        AgentWorkspaceStructuredPatch structuredPatch = AgentWorkspaceStructuredPatchBuilder.CreateOverwritePatch(originalFile, content);

        AgentWorkspaceWriteToolResult result = new()
        {
            Type = originalFile is null ? AgentWorkspaceWriteToolResultType.Create : AgentWorkspaceWriteToolResultType.Update,
            FilePath = file.FullPath,
            Additions = structuredPatch.Additions,
            Deletions = structuredPatch.Deletions,
            StructuredPatch = structuredPatch.Hunks,
            OriginalFile = originalFile,
        };

        AgentWorkspaceWriteToolResult.TryAdd(callId, result);
        return new TextContent(originalFile is null
            ? $"File created successfully at: '{file.FullPath}'"
            : $"The file '{file.FullPath}' has been updated successfully.");
    }
}
