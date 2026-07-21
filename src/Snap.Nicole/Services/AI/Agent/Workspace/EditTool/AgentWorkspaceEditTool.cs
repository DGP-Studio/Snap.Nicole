using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using Snap.Nicole.Core.Text;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal sealed class AgentWorkspaceEditTool(AgentWorkspaceContext context) : AgentWorkspaceTool(context)
{
    public override AITool Tool { get => field ??= AIFunctionFactory.Create(InvokeAsync).AsApprovalRequired(); }

    [DisplayName(Prompt.EditToolName)]
    [Description(Prompt.EditToolDescription)]
    public Task<AIContent> InvokeAsync(
        [Description("The absolute file to the file to modify")] string filePath,
        [Description("The text to replace")] string oldString,
        [Description("The text to replace it with (must be different from oldString)")] string newString,
        [Description("Replace all occurrences of oldString (default false)")] bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        return EditAsync(filePath, oldString, newString, replaceAll, cancellationToken);
    }

    private async Task<AIContent> EditAsync(string filePath, string oldString, string newString, bool replaceAll, CancellationToken cancellationToken)
    {
        MessageResult<AgentWorkspaceFile> result = Context.ResolveWorkspaceFile(filePath);
        if (result is string message)
        {
            return new TextContent(message);
        }

        if (result is not AgentWorkspaceFile file)
        {
            throw new InvalidOperationException("Unexpected result from ResolveWorkspaceFile");
        }

        ValidationResult validationResult = await ValidateInputAsync(file, oldString, newString, cancellationToken);
        if (!validationResult.Result)
        {
            return new TextContent(validationResult.Message);
        }

        AIContent content = await AgentWorkspaceEditToolResultFactory.CreateAsync(CallId, file, oldString, newString, replaceAll, cancellationToken);
        await Context.MarkFileAsReadAsync(file, cancellationToken);
        return content;
    }

    private async Task<ValidationResult> ValidateInputAsync(AgentWorkspaceFile file, string oldString, string newString, CancellationToken cancellationToken)
    {
        if (string.Equals(oldString, newString, StringComparison.Ordinal))
        {
            return ValidationResult.Ask("No changes to make: oldString and newString are exactly the same.");
        }

        // File doesn't exist
        if (!file.Exists)
        {
            // Empty oldString on nonexistent file means new file creation — valid
            if (string.IsNullOrEmpty(oldString))
            {
                return ValidationResult.Ok;
            }

            StringBuilder message = new();
            message.AppendLine("File does not exist. Available workspace roots:");
            foreach (AgentWorkspaceRootDirectory rootDirectory in Context.RootDirectories)
            {
                message.AppendLine($"- {rootDirectory.FullPath}");
            }

            if (Context.SuggestPathUnderWorkspaceRoots(file) is { } cwdSuggestion)
            {
                message.Append($"Did you mean {cwdSuggestion}?");
            }
            else if (AgentWorkspaceContext.FindSimilarFile(file) is { } similarFileName)
            {
                // Try to find a similar file with a different extension
                message.Append($"Did you mean {similarFileName}?");
            }

            return ValidationResult.Ask(message.ToString());
        }

        // File exists with empty oldString — only valid if file is empty
        if (string.IsNullOrEmpty(oldString))
        {
            // Only reject if the file has content (for file creation attempt)
            if (!await file.IsEmptyOrWhitespaceAsync(Encoding.UTF8WithoutBOM, cancellationToken))
            {
                return ValidationResult.Ask("Cannot create new file - file already exists.");
            }

            // Empty file with empty oldString is valid - we're replacing empty with content
            return ValidationResult.Ok;
        }

        if (await Context.FileModifiedSinceLastReadAsync(file, cancellationToken))
        {
            return ValidationResult.Ask("File has been modified since read, either by the user or by a linter. Read it again before attempting to write it.");
        }

        return ValidationResult.Ok;
    }
}
