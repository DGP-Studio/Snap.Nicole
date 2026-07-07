using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Threading;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;

// See https://github.com/claude-code-best/claude-code/blob/main/packages/builtin-tools/src/tools/GlobTool/GlobTool.ts
internal sealed class AgentWorkspaceGlobTool(AgentWorkspaceContext context)
    : AgentWorkspaceTool(context)
{
    public override AITool Tool { get => field ??= AIFunctionFactory.Create(Invoke); }

    [DisplayName(Prompt.GlobToolName)]
    [Description(Prompt.GlobToolDescription)]
    public AIContent Invoke(
        [Description("The glob pattern to match files against")] string pattern,
        [Description("""The directory to search in. If not specified, the current working directory will be used. IMPORTANT: Omit this field to use the default directory. DO NOT enter "undefined" or "null" - simply omit it for the default behavior. Must be a valid directory path if provided.""")][DefaultValue(null)] string? path,
        CancellationToken cancellationToken = default)
    {
        return Glob(pattern, path, cancellationToken);
    }

    private AIContent Glob(string pattern, string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Glob pattern cannot be empty.", nameof(pattern));
        }

        AgentWorkspaceDirectory globDirectory = Context.ResolveGlobDirectoryPath(path);
        return AgentWorkspaceGlobToolResultFactory.Create(globDirectory, pattern, cancellationToken);
    }
}
