using Microsoft.Agents.AI;
using Snap.Nicole.Services.AI.Agent.Workspace.EditTool;
using Snap.Nicole.Services.AI.Agent.Workspace.GlobTool;
using Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;
using Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;
using Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;
using Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;
using System.Collections.Generic;
using System.Linq;
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
            Tools = toolComponents.Select(static component => component.Tool),
        };

        return ValueTask.FromResult(aiContext);
    }

    private string CreateInstructions()
    {
        IReadOnlyList<AgentWorkspaceRootDirectory> rootDirectories = workspaceContext.RootDirectories;
        StringBuilder builder = new();
        builder.AppendLine("## Workspace");

        if (rootDirectories.Count > 1)
        {
            builder.AppendLine("You have access to following workspaces");
        }
        else
        {
            builder.AppendLine("You have access to following workspace");
        }


        builder.AppendLine("Primary directory:");
        builder.AppendLine($"- {rootDirectories[0].FullPath}");

        if (rootDirectories.Count > 1)
        {
            builder.AppendLine("Additional directories:");
            foreach (AgentWorkspaceRootDirectory rootDirectory in rootDirectories.Skip(1))
            {
                builder.AppendLine($"- {rootDirectory.FullPath}");
            }
        }

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
            new AgentWorkspaceShellTool(workspaceContext),
        ];
    }
}
