using Microsoft.Extensions.AI;
using Snap.Nicole.Core.Text;
using Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;

internal static class AgentWorkspaceWriteToolResultFactory
{
    public static async Task<AIContent> CreateAsync(string callId, AgentWorkspaceFile file, string content, CancellationToken cancellationToken)
    {
        string? originalFile = file.Exists
            ? await file.ReadAllTextAsync(Encoding.UTF8WithoutBOM, cancellationToken)
            : null;

        file.Directory.CreateDirectory();
        await file.WriteAllTextAsync(content, Encoding.UTF8WithoutBOM, cancellationToken);

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
