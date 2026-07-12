using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;

internal sealed class AgentWorkspaceWriteTool(AgentWorkspaceContext context) : AgentWorkspaceTool(context)
{
    public override AITool Tool { get => field ??= AIFunctionFactory.Create(InvokeAsync).AsApprovalRequired(); }

    [DisplayName(Prompt.WriteToolName)]
    [Description(Prompt.WriteToolDescription)]
    public Task<AIContent> InvokeAsync(
        [Description("The absolute path to the file to write (must be absolute, not relative)")] string filePath,
        [Description("The content to write to the file")] string content,
        CancellationToken cancellationToken = default)
    {
        return WriteAsync(filePath, content, cancellationToken);
    }

    private async Task<AIContent> WriteAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        MessageResult<AgentWorkspaceFile> result = Context.ResolveWorkspaceFile(filePath);
        if (result is string message)
        {
            return new TextContent(message);
        }

        if (result is AgentWorkspaceFile file)
        {
            ValidationResult validationResult = await ValidateInputAsync(file, cancellationToken);
            if (!validationResult.Result)
            {
                return new TextContent(validationResult.Message);
            }

            AIContent resultContent = await AgentWorkspaceWriteToolResultFactory.CreateAsync(CallId, file, content, cancellationToken);
            await Context.MarkFileAsReadAsync(file, cancellationToken);
            return resultContent;
        }

        throw new InvalidOperationException("Unexpected result from ResolveWorkspaceFile.");
    }

    private async Task<ValidationResult> ValidateInputAsync(AgentWorkspaceFile file, CancellationToken cancellationToken)
    {
        if (Directory.Exists(file.FullPath))
        {
            return ValidationResult.Ask($"'{file.FullPath}' is a directory not a file.");
        }

        if (!file.Exists)
        {
            return ValidationResult.Ok;
        }

        if (await Context.FileModifiedSinceLastReadAsync(file, cancellationToken))
        {
            return ValidationResult.Ask("File has been modified since read, either by the user or by a linter. Read it again before attempting to write it.");
        }

        return ValidationResult.Ok;
    }
}
