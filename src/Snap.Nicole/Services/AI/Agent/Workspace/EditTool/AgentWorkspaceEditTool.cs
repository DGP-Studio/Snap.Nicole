using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using Snap.Nicole.Core.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal sealed class AgentWorkspaceEditTool(AgentWorkspaceContext context) : AgentWorkspaceTool(context)
{
    private const int LineEndingKindCount = 7;

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
        string fileText = await File.ReadAllTextAsync(file.FullPath, Encoding.UTF8WithoutBOM, cancellationToken);

        NormalizedString normalizedFileText = new(fileText);
        NormalizedString normalizedOldString = new(oldString);
        NormalizedString normalizedNewString = new(newString);

        IReadOnlyList<int> matchStartIndexes = FindMatchStartIndexes(normalizedFileText.Value, normalizedOldString.Value);

        if (matchStartIndexes.Count is 0)
        {
            string message = $"""
                String to replace not found in file.
                String: '{oldString}'
                """;
            AgentWorkspaceException.Throw(message);
        }

        if (matchStartIndexes.Count > 1 && !replaceAll)
        {
            string message = $"""
                Found multiple matches of the string to replace, but replaceAll is false.
                To replace all occurrences, set replaceAll to true.
                To replace only one occurrence, please provide more context to uniquely identify the instance.
                String: {oldString}
                """;
            AgentWorkspaceException.Throw(message);
        }

        string normalizedUpdatedFileText = replaceAll
            ? normalizedFileText.Value.Replace(normalizedOldString.Value, normalizedNewString.Value, StringComparison.Ordinal)
            : normalizedFileText.Value.Remove(matchStartIndexes[0], normalizedOldString.Value.Length).Insert(matchStartIndexes[0], normalizedNewString.Value);
        string updatedFileText = (GetDominantLineEnding(fileText) ?? GetDominantLineEnding(newString)) is { } lineEnding
            ? normalizedUpdatedFileText.ReplaceLineEndings(lineEnding)
            : normalizedUpdatedFileText;
        AgentWorkspaceEditToolResult result = AgentWorkspaceEditToolResultFactory.Create(file, normalizedFileText, normalizedOldString, normalizedNewString, matchStartIndexes);

        await File.WriteAllTextAsync(file.FullPath, updatedFileText, Encoding.UTF8WithoutBOM, cancellationToken);
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
                await Context.MarkFileAsReadAsync(file, cancellationToken);
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

    private static IReadOnlyList<int> FindMatchStartIndexes(NormalizedString fileText, NormalizedString oldString)
    {
        List<int> matchStartIndexes = [];

        int searchStartIndex = 0;
        while (searchStartIndex < fileText.Length)
        {
            int matchStartIndex = fileText.IndexOf(oldString, searchStartIndex, StringComparison.Ordinal);
            if (matchStartIndex < 0)
            {
                break;
            }

            matchStartIndexes.Add(matchStartIndex);
            searchStartIndex = matchStartIndex + oldString.Length;
        }

        return matchStartIndexes;
    }

    private static string? GetDominantLineEnding(string value)
    {
        int[] counts = new int[LineEndingKindCount];
        int[] firstIndexes = new int[LineEndingKindCount];
        Array.Fill(firstIndexes, int.MaxValue);

        int currentLineEndingIndex = 0;
        for (int i = 0; i < value.Length; i++)
        {
            int kind = GetLineEndingKind(value, ref i);
            if (kind < 0)
            {
                continue;
            }

            if (counts[kind] is 0)
            {
                firstIndexes[kind] = currentLineEndingIndex;
            }

            counts[kind]++;
            currentLineEndingIndex++;
        }

        int dominantKind = -1;
        int dominantCount = 0;
        int dominantFirstIndex = int.MaxValue;
        for (int i = 0; i < LineEndingKindCount; i++)
        {
            if (counts[i] > dominantCount || counts[i] == dominantCount && firstIndexes[i] < dominantFirstIndex)
            {
                dominantKind = i;
                dominantCount = counts[i];
                dominantFirstIndex = firstIndexes[i];
            }
        }

        return dominantKind < 0 ? null : GetLineEndingText(dominantKind);
    }

    private static int GetLineEndingKind(string value, ref int index)
    {
        switch (value[index])
        {
            case '\r':
                if (index + 1 < value.Length && value[index + 1] is '\n')
                {
                    index++;
                    return 0;
                }

                return 1;
            case '\n':
                return 2;
            case '\f':
                return 3;
            case '\u0085':
                return 4;
            case '\u2028':
                return 5;
            case '\u2029':
                return 6;
            default:
                return -1;
        }
    }

    private static string GetLineEndingText(int lineEndingKind)
    {
        return lineEndingKind switch
        {
            0 => "\r\n",
            1 => "\r",
            2 => "\n",
            3 => "\f",
            4 => "\u0085",
            5 => "\u2028",
            6 => "\u2029",
            _ => throw new ArgumentOutOfRangeException(nameof(lineEndingKind)),
        };
    }
}
