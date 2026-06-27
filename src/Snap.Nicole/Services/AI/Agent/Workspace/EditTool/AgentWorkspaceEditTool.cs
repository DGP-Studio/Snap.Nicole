using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using Snap.Nicole.Core.IO;
using Snap.Nicole.Core.IO.StreamingText;
using Snap.Nicole.Core.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal sealed class AgentWorkspaceEditTool(AgentWorkspaceContext context)
    : AgentWorkspaceTool(context)
{
    private const int WriterBufferSize = 4096;

    public override AITool Tool { get => field ??= AIFunctionFactory.Create(InvokeAsync).AsApprovalRequired(); }

    [DisplayName(Prompt.EditToolName)]
    [Description(Prompt.EditToolDescription)]
    public async Task<AIContent> InvokeAsync(
        [Description("The absolute file to the file to modify")] string filePath,
        [Description("The text to replace")] string oldString,
        [Description("The text to replace it with (must be different from oldString)")] string newString,
        [Description("Replace all occurrences of oldString (default false)")] bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        return new TextContent(await EditAsync(filePath, oldString, newString, replaceAll, cancellationToken));
    }

    private static async Task<AgentWorkspaceEditToolResult> EditFileAsync(AgentWorkspaceFile file, string oldString, string newString, bool replaceAll, CancellationToken cancellationToken)
    {
        AgentWorkspaceEditToolResult result;
        NormalizedString normalizedOldString = new(oldString);
        NormalizedString normalizedNewString = new(newString);

        using (TemporaryFileStream temporaryStream = file.CreateTemporary())
        {
            IReadOnlyList<StreamingTextMatch> matches;
            using (FileStream sourceStream = file.OpenRead())
            {
                using (TextReader reader = new LineEndingNormalizingTextReader(new StreamReader(sourceStream, Encoding.UTF8WithoutBOM, true)))
                {
                    using (StreamWriter writer = new(temporaryStream, Encoding.UTF8WithoutBOM, WriterBufferSize, true))
                    {
                        try
                        {
                            matches = await StreamingText.ReplaceAsync(reader, writer, normalizedOldString.Value, normalizedNewString.Value, replaceAll, cancellationToken);
                        }
                        catch (StreamingTextMultipleMatchesException ex)
                        {
                            string message = $"""
                                Found multiple matches of the string to replace, but replaceAll is false.
                                To replace all occurrences, set replaceAll to true.
                                To replace only one occurrence, please provide more context to uniquely identify the instance.
                                String: {oldString}
                                """;
                            throw AgentWorkspaceException.Throw(message, ex);
                        }
                    }
                }
            }

            if (matches.Count is 0)
            {
                string message = $"""
                    String to replace not found in file.
                    String: '{oldString}'
                    """;
                AgentWorkspaceException.Throw(message);
            }

            result = await AgentWorkspaceEditToolResultFactory.CreateAsync(file, normalizedOldString, normalizedNewString, matches, cancellationToken);
            temporaryStream.Commit();
        }

        return result;
    }

    private async Task<string> EditAsync(string filePath, string oldString, string newString, bool replaceAll, CancellationToken cancellationToken)
    {
        MessageResult<AgentWorkspaceFile> result = Context.ResolveWorkspaceFile(filePath);
        if (result is string message)
        {
            return message;
        }

        if (result is AgentWorkspaceFile file)
        {
            ValidationResult validationResult = await ValidateInputAsync(file, oldString, newString, cancellationToken);
            if (!validationResult.Result)
            {
                return validationResult.Message;
            }

            // TODO: for empty oldString, we should check if the file exists and is empty, and if so, allow the replacement to create a new file with newString.
            ArgumentException.ThrowIfNullOrEmpty(oldString, "oldString cannot be empty", nameof(oldString));

            try
            {
                AgentWorkspaceEditToolResult.TryAdd(CallId, await EditFileAsync(file, oldString, newString, replaceAll, cancellationToken));
                Context.MarkFileAsRead(file.FullPath, await file.ComputeHashAsync(cancellationToken));
            }
            catch (AgentWorkspaceException ex)
            {
                return ex.Message;
            }

            return replaceAll
                ? $"The file {file.FullPath} has been updated. All occurrences were successfully replaced."
                : $"The file {file.FullPath} has been updated successfully.";
        }

        throw new InvalidOperationException("Unexpected result from ResolveWorkspaceFile");
    }

    private async Task<ValidationResult> ValidateInputAsync(AgentWorkspaceFile file, string oldString, string newString, CancellationToken cancellationToken)
    {
        if (string.Equals(oldString, newString, StringComparison.Ordinal))
        {
            return ValidationResult.Ask("No changes to make: oldString and newString are exactly the same.", 1);
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
                // Try to find a similar file with a different extension
                message.Append($"Did you mean {similarFileName}?");
            }

            return ValidationResult.Ask(message.ToString(), 4);
        }

        // File exists with empty oldString — only valid if file is empty
        if (string.IsNullOrEmpty(oldString))
        {
            // Only reject if the file has content (for file creation attempt)
            if (!await file.IsEmptyOrWhitespaceAsync(Encoding.UTF8WithoutBOM, cancellationToken))
            {
                return ValidationResult.Ask("Cannot create new file - file already exists.", 3);
            }

            // Empty file with empty oldString is valid - we're replacing empty with content
            return ValidationResult.Ok;
        }

        if (await Context.FileModifiedSinceLastReadAsync(file.FullPath, cancellationToken))
        {
            return ValidationResult.Ask("File has been modified since read, either by the user or by a linter. Read it again before attempting to write it.", 7);
        }

        return ValidationResult.Ok;
    }
}
