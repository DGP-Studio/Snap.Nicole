using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFileAccessWriteTool : AgentWorkspaceFileAccessToolComponent
{
    private readonly AITool tool;

    public AgentWorkspaceFileAccessWriteTool(AgentWorkspaceFileAccessContext context) : base(context)
    {
        tool = AIFunctionFactory.Create(WriteAsync).AsApprovalRequired();
    }

    public override AITool Tool { get => tool; }

    [DisplayName(AgentWorkspaceFileAccessToolNames.Write)]
    [Description("""
        Writes a file to the local filesystem, overwriting if one exists.

        When to use: creating a new file, or fully replacing one you've already Read. Overwriting an existing file you haven't Read will fail. For partial changes, use Edit instead.
        """)]
    private async Task<string> WriteAsync([Description("The absolute path to the file to write (must be absolute, not relative)")] string file_path, [Description("The content to write to the file")] string content, CancellationToken cancellationToken = default)
    {
        AgentWorkspacePath path = Context.ResolveAbsoluteFilePath(file_path);
        if (Directory.Exists(path.FullPath))
        {
            throw new InvalidOperationException($"'{path.DisplayPath}' is a directory. Write requires a file path.");
        }

        if (File.Exists(path.FullPath) && !Context.WasFileRead(path.FullPath))
        {
            throw new InvalidOperationException($"File '{path.DisplayPath}' must be read with {AgentWorkspaceFileAccessToolNames.Read} before overwriting.");
        }

        string? directory = Path.GetDirectoryName(path.FullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path.FullPath, content, Encoding.UTF8, cancellationToken);
        Context.MarkFileAsRead(path.FullPath);
        return $"File '{path.DisplayPath}' written.";
    }
}
