using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Snap.Nicole.Core.Text;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceWriteTool : AgentWorkspaceToolComponent
{
    private readonly AITool tool;

    public AgentWorkspaceWriteTool(AgentWorkspaceContext context) : base(context)
    {
        tool = AIFunctionFactory.Create(WriteAsync).AsApprovalRequired();
    }

    public override AITool Tool { get => tool; }

    [DisplayName(Prompt.WriteToolName)]
    [Description("""
        Writes a file to the local filesystem, overwriting if one exists.

        When to use: creating a new file, or fully replacing one you've already Read. Overwriting an existing file you haven't Read, or one whose content changed after Read, will fail. For partial changes, use Edit instead.
        """)]
    private async Task<string> WriteAsync([Description("The absolute path to the file to write (must be absolute, not relative)")] string file_path, [Description("The content to write to the file")] string content, CancellationToken cancellationToken = default)
    {
        AgentWorkspacePath path = Context.ResolveAbsoluteFilePath(file_path);
        if (Directory.Exists(path.FullPath))
        {
            throw new InvalidOperationException($"'{path.FullPath}' is a directory. Write requires a file path.");
        }

        if (File.Exists(path.FullPath))
        {
            await Context.ValidateFileReadForModificationAsync(path.FullPath, cancellationToken);
        }

        string? directory = Path.GetDirectoryName(path.FullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path.FullPath, content, Encoding.UTF8WithoutBOM, cancellationToken);
        Context.InvalidateFileRead(path.FullPath);
        return $"File '{path.FullPath}' written.";
    }
}
