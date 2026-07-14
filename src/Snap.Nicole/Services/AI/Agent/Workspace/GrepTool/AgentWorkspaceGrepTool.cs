using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal sealed class AgentWorkspaceGrepTool(AgentWorkspaceContext context)
    : AgentWorkspaceTool(context)
{
    public override AITool Tool { get => field ??= AIFunctionFactory.Create(InvokeAsync); }

    [DisplayName(Prompt.GrepToolName)]
    [Description(Prompt.GrepToolDescription)]
    public Task<AIContent> InvokeAsync(
        [Description("The regular expression pattern to search for in file contents")] string pattern,
        [Description("Absolute file or directory to search. Defaults to current working directory.")][DefaultValue(null)] string? path,
        [Description("""Glob pattern to filter files (e.g. "*.js", "*.{ts,tsx}")""")][DefaultValue(null)] string? glob,
        [Description("""Output mode: "content" shows matching lines (supports context and line numbers), "files_with_matches" shows file paths, "count" shows match counts. Defaults to "files_with_matches".""")][DefaultValue(AgentWorkspaceGrepToolOutputMode.FilesWithMatches)] AgentWorkspaceGrepToolOutputMode outputMode,
        [Description("""Number of lines to show before each match. Requires outputMode: "content", ignored otherwise.""")][DefaultValue(null)] int? beforeContext,
        [Description("""Number of lines to show after each match. Requires outputMode: "content", ignored otherwise.""")][DefaultValue(null)] int? afterContext,
        [Description("""Number of lines to show before and after each match. Requires outputMode: "content", ignored otherwise.""")][DefaultValue(null)] int? context,
        [Description("""Show line numbers in output. Requires outputMode: "content", ignored otherwise. Defaults to true.""")][DefaultValue(true)] bool showLineNumbers,
        [Description("Case insensitive search")][DefaultValue(false)] bool ignoreCase,
        [Description("""Limit output to first N lines/entries. Works across all output modes. Defaults to 250 when unspecified. Pass 0 for unlimited (use sparingly - large result sets waste context).""")][DefaultValue(250)] int? headLimit,
        [Description("""Skip first N lines/entries before applying head_limit. Works across all output modes. Defaults to 0.""")][DefaultValue(0)] int offset,
        [Description("Enable multiline mode where . matches newlines and patterns can span lines. Default: false.")][DefaultValue(false)] bool multiline,
        [Description("""Print only the matched (non-empty) parts of each matching line, one match per output line. Requires outputMode: "content", ignored otherwise. Defaults to false.""")][DefaultValue(false)] bool onlyMatching,
        CancellationToken cancellationToken = default)
    {
        return GrepAsync(pattern, path, glob, outputMode, beforeContext, afterContext, context, showLineNumbers, ignoreCase, headLimit, offset, multiline, onlyMatching, cancellationToken);
    }

    private async Task<AIContent> GrepAsync(string pattern, string? path, string? glob, AgentWorkspaceGrepToolOutputMode outputMode, int? beforeContext, int? afterContext, int? context, bool showLineNumbers, bool ignoreCase, int? headLimit, int offset, bool multiline, bool onlyMatching, CancellationToken cancellationToken)
    {
        MessageResult<AgentWorkspaceGrepToolOptions> optionsResult = AgentWorkspaceGrepToolOptions.Create(pattern, glob, outputMode, beforeContext, afterContext, context, showLineNumbers, ignoreCase, headLimit, offset, multiline, onlyMatching);
        if (optionsResult is string optionsMessage)
        {
            return new TextContent(optionsMessage);
        }

        if (optionsResult is not AgentWorkspaceGrepToolOptions options)
        {
            throw new InvalidOperationException("Unexpected result from AgentWorkspaceGrepToolOptions.Create.");
        }

        MessageResult<AgentWorkspacePath> searchPathResult = Context.ResolveGrepSearchPath(path);
        if (searchPathResult is string searchPathMessage)
        {
            return new TextContent(searchPathMessage);
        }

        if (searchPathResult is not AgentWorkspacePath searchPath)
        {
            throw new InvalidOperationException("Unexpected result from ResolveGrepSearchPath.");
        }

        return await AgentWorkspaceGrepToolResultFactory.CreateAsync(CallId, searchPath, options, cancellationToken);
    }
}
