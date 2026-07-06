using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

internal sealed class AgentWorkspaceReadTool(AgentWorkspaceContext context) : AgentWorkspaceTool(context)
{
    public override AITool Tool { get => field ??= AIFunctionFactory.Create(InvokeAsync); }

    [DisplayName(Prompt.ReadToolName)]
    [Description(Prompt.ReadToolDescription)]
    public Task<AIContent> InvokeAsync(
        [Description("The absolute path to the file to read")] string filePath,
        [Description("The line number to start reading from. Only provide if the file is too large to read at once")][DefaultValue(0)] int offset,
        [Description("The number of lines to read. Only provide if the file is too large to read at once.")][DefaultValue(null)] int? limit,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(filePath, offset, limit, cancellationToken);
    }

    private ValidationResult ValidateInput(AgentWorkspaceFile file)
    {
        if (!file.Exists)
        {
            StringBuilder message = new();
            message.AppendLine($"File '{file.FullPath}' not found.");
            message.AppendLine("Available workspace roots:");
            foreach (string rootDirectory in Context.RootDirectories)
            {
                message.AppendLine($"- {rootDirectory}");
            }

            if (Context.SuggestPathUnderWorkspaceRoots(file.FullPath) is { } cwdSuggestion)
            {
                message.Append($"Did you mean {cwdSuggestion}?");
            }
            else if (AgentWorkspaceContext.FindSimilarFile(file.FullPath) is { } similarFileName)
            {
                message.Append($"Did you mean {similarFileName}?");
            }

            return ValidationResult.Ask(message.ToString(), 4);
        }

        FileInfo fileInfo = new(file.FullPath);
        if (fileInfo.Length is 0)
        {
            return ValidationResult.Ask($"File '{file.FullPath}' is empty.", 4);
        }

        string extension = Path.GetExtension(file.FullPath);
        if (AgentWorkspaceReadToolResultFactory.IsBinaryExtension(extension) && !AgentWorkspaceReadToolResultFactory.IsSupportedImageExtension(extension))
        {
            return ValidationResult.Ask($"This tool cannot read binary files. The file appears to be a binary {extension.ToLowerInvariant()} file. Please use appropriate tools for binary file analysis.", 4);
        }

        return ValidationResult.Ok;
    }

    private async Task<AIContent> ReadAsync(string filePath, int offset, int? limit, CancellationToken cancellationToken = default)
    {
        MessageResult<AgentWorkspaceFile> result = Context.ResolveWorkspaceFile(filePath);
        if (result is string message)
        {
            return new TextContent(message);
        }

        if (result is AgentWorkspaceFile file)
        {
            ValidationResult validationResult = ValidateInput(file);
            if (!validationResult.Result)
            {
                return new TextContent(validationResult.Message);
            }

            AIContent content = await AgentWorkspaceReadToolResultFactory.CreateAsync(file, offset, limit, cancellationToken);
            await Context.MarkFileAsReadAsync(file, cancellationToken);
            return content;
        }

        throw new InvalidOperationException("Unexpected result from ResolveWorkspaceFile.");
    }
}
