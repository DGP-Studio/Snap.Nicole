using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.EditTool;
using Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;
using Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;
using Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;
using Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceContextProvider : AIContextProvider
{
    private readonly AgentWorkspaceContext workspaceContext;
    private readonly IReadOnlyList<AgentWorkspaceTool> toolComponents;

    public AgentWorkspaceContextProvider(IReadOnlyList<string> workingDirectories)
    {
        workspaceContext = new(workingDirectories);
        toolComponents = CreateToolComponents(workspaceContext);
    }

    public override IReadOnlyList<string> StateKeys { get => []; }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        AIContext aiContext = new()
        {
            Instructions = CreateInstructions(),
            Tools = CreateTools(toolComponents),
        };

        return new(aiContext);
    }

    private string CreateInstructions()
    {
        IReadOnlyList<string> rootDirectories = workspaceContext.RootDirectories;
        StringBuilder builder = new();
        builder.AppendLine("## Workspace File Access");
        builder.AppendLine("You have access to workspace files via `Read` for reading files, `Write` for full file replacement, `Edit` for exact string replacement, `Glob` for file pattern matching, and `Grep` for content search.");
        builder.AppendLine("These files persist beyond the current session and may be shared by future sessions that use the same workspace.");
        builder.AppendLine("Use these tools to read input data provided by the user, write output artifacts, and manage files the user has asked you to work with.");
        builder.AppendLine();
        if (rootDirectories.Count is 1)
        {
            builder.AppendLine("The workspace root is:");
            builder.AppendLine(rootDirectories[0]);
            builder.AppendLine("Use absolute paths with `Read`, `Write`, and `Edit`. For `Glob` and `Grep`, omit `path` to search this root, or provide an absolute path.");
        }
        else
        {
            builder.AppendLine("The accessible workspace roots are:");
            foreach (string rootDirectory in rootDirectories)
            {
                builder.AppendLine($"- {rootDirectory}");
            }

            builder.AppendLine("Use absolute paths with `Read`, `Write`, and `Edit`. For `Glob` and `Grep`, omit `path` to search the first workspace root, or provide an absolute path.");
        }

        builder.AppendLine();
        builder.AppendLine("- Never overwrite existing files unless the user has explicitly asked you to do so. Writing and editing files require explicit user approval.");
        builder.AppendLine($"- Use `{Prompt.ReadToolName}` to read files. `Read.file_path` must be an absolute path under a workspace root. Text results use cat -n line numbers. Image results are returned as visual content.");
        builder.AppendLine("- File paths returned by these tools are full paths.");
        builder.AppendLine("- Use `Write` to create a new file or fully replace a file already read with `Read`. Overwriting an unread existing file fails. Overwriting a file whose content changed after `Read` fails until it is read again. Use `Edit` for partial changes.");
        builder.AppendLine($"- Before using `Edit`, read the target file with `{Prompt.ReadToolName}`. `Edit.file_path` must be an absolute path under a workspace root.");
        builder.AppendLine("- `Edit` fails if the target file content changed after it was read; read the file again before retrying.");
        builder.AppendLine("- For `Edit`, `old_string` must match the file exactly and must be unique unless `replace_all` is true.");
        builder.AppendLine("- Do not re-read a file immediately after `Edit` just to verify the edit.");
        builder.AppendLine("- For `Glob`, omit `path` to search the current workspace directory. If `path` is provided, it must resolve to a directory under a workspace root.");
        builder.AppendLine("- For `Grep`, omit `path` to search the current workspace directory. If `path` is provided, it must resolve to a file or directory under a workspace root.");
        builder.AppendLine("- Paths outside workspace roots and parent-directory traversal are rejected.");
        builder.Append("- Files may be organized into subdirectories. Use `Glob` to find files by path pattern or `Grep` to search file contents.");
        return builder.ToString();
    }

    private static IReadOnlyList<AgentWorkspaceTool> CreateToolComponents(AgentWorkspaceContext workspaceContext)
    {
        return
        [
            new AgentWorkspaceWriteTool(workspaceContext),
            new AgentWorkspaceReadTool(workspaceContext),
            new AgentWorkspaceEditTool(workspaceContext),
            new AgentWorkspaceGlobTool(workspaceContext),
            new AgentWorkspaceGrepTool(workspaceContext),
        ];
    }

    private static IReadOnlyList<AITool> CreateTools(IReadOnlyList<AgentWorkspaceTool> components)
    {
        List<AITool> createdTools = new(components.Count);
        foreach (AgentWorkspaceTool component in components)
        {
            createdTools.Add(component.Tool);
        }

        return createdTools;
    }
}
