using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFileAccessProvider : AIContextProvider
{
    private readonly AgentWorkspaceFileAccessContext fileAccessContext;
    private readonly IReadOnlyList<AgentWorkspaceFileAccessToolComponent> toolComponents;
    private readonly IReadOnlyList<AITool> tools;

    public AgentWorkspaceFileAccessProvider(IReadOnlyList<string> workingDirectories)
    {
        fileAccessContext = new(workingDirectories);
        toolComponents = CreateToolComponents(fileAccessContext);
        tools = CreateTools(toolComponents);
    }

    public override IReadOnlyList<string> StateKeys { get => []; }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        AIContext aiContext = new()
        {
            Instructions = CreateInstructions(),
            Tools = tools,
        };

        return new(aiContext);
    }

    private string CreateInstructions()
    {
        IReadOnlyList<string> rootDirectories = fileAccessContext.RootDirectories;
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
        builder.AppendLine($"- Use `{AgentWorkspaceFileAccessToolNames.Read}` to read files. `Read.file_path` must be an absolute path under a workspace root. Text and notebook results use cat -n line numbers.");
        builder.AppendLine("- Image and PDF visual rendering is not available in this provider. Do not treat image or PDF read failures as text-file failures.");
        builder.AppendLine("- File paths returned by these tools are full paths.");
        builder.AppendLine("- Use `Write` to create a new file or fully replace a file already read with `Read`. Overwriting an unread existing file fails. Use `Edit` for partial changes.");
        builder.AppendLine($"- Before using `Edit`, read the target file with `{AgentWorkspaceFileAccessToolNames.Read}`. `Edit.file_path` must be an absolute path under a workspace root.");
        builder.AppendLine("- For `Edit`, `old_string` must match the file exactly and must be unique unless `replace_all` is true.");
        builder.AppendLine("- Do not re-read a file immediately after `Edit` just to verify the edit.");
        builder.AppendLine("- For `Glob`, omit `path` to search the current workspace directory. If `path` is provided, it must resolve to a directory under a workspace root.");
        builder.AppendLine("- For `Grep`, omit `path` to search the current workspace directory. If `path` is provided, it must resolve to a file or directory under a workspace root.");
        builder.AppendLine("- Paths outside workspace roots and parent-directory traversal are rejected.");
        builder.Append("- Files may be organized into subdirectories. Use `Glob` to find files by path pattern or `Grep` to search file contents.");
        return builder.ToString();
    }

    private static IReadOnlyList<AgentWorkspaceFileAccessToolComponent> CreateToolComponents(AgentWorkspaceFileAccessContext fileAccessContext)
    {
        return
        [
            new AgentWorkspaceFileAccessWriteTool(fileAccessContext),
            new AgentWorkspaceFileAccessReadTool(fileAccessContext),
            new AgentWorkspaceFileAccessEditTool(fileAccessContext),
            new AgentWorkspaceFileAccessGlobTool(fileAccessContext),
            new AgentWorkspaceFileAccessGrepTool(fileAccessContext),
        ];
    }

    private static IReadOnlyList<AITool> CreateTools(IReadOnlyList<AgentWorkspaceFileAccessToolComponent> components)
    {
        List<AITool> createdTools = new(components.Count);
        foreach (AgentWorkspaceFileAccessToolComponent component in components)
        {
            createdTools.Add(component.Tool);
        }

        return createdTools;
    }
}
