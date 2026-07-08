using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;

// See https://github.com/claude-code-best/claude-code/blob/main/packages/builtin-tools/src/tools/GlobTool/GlobTool.ts
internal sealed class AgentWorkspaceGlobTool(AgentWorkspaceContext context)
    : AgentWorkspaceTool(context)
{
    public override AITool Tool { get => field ??= AIFunctionFactory.Create(Invoke); }

    [DisplayName(Prompt.GlobToolName)]
    [Description(Prompt.GlobToolDescription)]
    public Task<AIContent> Invoke(
        [Description("The glob pattern to match files against")] string pattern,
        [Description("""The directory to search in. If not specified, the current working directory will be used. IMPORTANT: Omit this field to use the default directory. DO NOT enter "undefined" or "null" - simply omit it for the default behavior. Must be a valid directory path if provided.""")][DefaultValue(null)] string? path,
        CancellationToken cancellationToken = default)
    {
        return GlobAsync(pattern, path, cancellationToken);
    }

    private async Task<AIContent> GlobAsync(string pattern, string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new TextContent("Glob pattern cannot be empty.");
        }

        MessageResult<AgentWorkspaceDirectory> result = Context.ResolveGlobDirectoryPath(path);
        if (result is string message)
        {
            return new TextContent(message);
        }

        if (result is not AgentWorkspaceDirectory globDirectory)
        {
            throw new InvalidOperationException("Unexpected result from ResolveGlobDirectoryPath.");
        }

        return await AgentWorkspaceGlobToolResultFactory.CreateAsync(CallId, globDirectory, pattern, cancellationToken);
    }
}
