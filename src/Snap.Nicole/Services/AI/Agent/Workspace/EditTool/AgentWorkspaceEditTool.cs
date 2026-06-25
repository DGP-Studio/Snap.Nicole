using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using Snap.Nicole.Core.IO;
using Snap.Nicole.Core.Text;
using System.Buffers;
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
    private const int StreamBufferSize = 4096;
    private const int OutputBufferFlushThreshold = 4096;

    public override AITool Tool { get => field ??= AIFunctionFactory.Create(EditAsync).AsApprovalRequired(); }

    private static int[] CreateKMPPrefixTable(ReadOnlySpan<char> value)
    {
        int[] prefixTable = new int[value.Length];
        int prefixLength = 0;
        for (int i = 1; i < value.Length; i++)
        {
            while (prefixLength > 0 && value[i] != value[prefixLength])
            {
                prefixLength = prefixTable[prefixLength - 1];
            }

            if (value[i] == value[prefixLength])
            {
                prefixLength++;
                prefixTable[i] = prefixLength;
            }
        }

        return prefixTable;
    }

    private static async Task<AgentWorkspaceEditToolResult> ReplaceAsync(AgentWorkspaceFile file, string oldString, string newString, bool replaceAll, CancellationToken cancellationToken)
    {
        AgentWorkspaceEditToolResult result;
        string normalizedOldString = oldString.ReplaceLineEndings("\n");
        string normalizedNewString = newString.ReplaceLineEndings("\n");
        using (TemporaryFileStream temporaryStream = file.CreateTemporary())
        {
            IReadOnlyList<AgentWorkspaceEditToolMatch> matches;
            using (FileStream sourceStream = file.OpenRead())
            {
                using (TextReader reader = new LineEndingNormalizingTextReader(new StreamReader(sourceStream, Encoding.UTF8WithoutBOM, true)))
                {
                    using (StreamWriter writer = new(temporaryStream, Encoding.UTF8WithoutBOM, StreamBufferSize, true))
                    {
                        matches = await ReplaceAsync(reader, writer, normalizedOldString, normalizedNewString, replaceAll, cancellationToken);
                    }
                }
            }

            if (matches.Count is 0)
            {
                string message = $"""
                    String to replace not found in file.
                    String: '{oldString}'
                    """;
                throw new InvalidOperationException(message);
            }

            result = await AgentWorkspaceEditToolResultFactory.CreateAsync(file, normalizedOldString, normalizedNewString, matches, cancellationToken);
            temporaryStream.Commit();
        }

        return result;
    }

    private static async Task<IReadOnlyList<AgentWorkspaceEditToolMatch>> ReplaceAsync(TextReader reader, StreamWriter writer, string oldString, string newString, bool replaceAll, CancellationToken cancellationToken)
    {
        List<AgentWorkspaceEditToolMatch> matches = [];

        using IMemoryOwner<char> readBufferOwner = MemoryPool<char>.Shared.Rent(StreamBufferSize);
        Memory<char> readBuffer = readBufferOwner.Memory[..StreamBufferSize];

        StringBuilder outputBuffer = new(OutputBufferFlushThreshold);

        int matchedLength = 0;
        long sourceCharacterIndex = 0;
        int[] prefixTable = CreateKMPPrefixTable(oldString);

        while (true)
        {
            int readCount = await reader.ReadAsync(readBuffer, cancellationToken);
            if (readCount is 0)
            {
                break;
            }

            for (int i = 0; i < readCount; i++)
            {
                char current = readBuffer.Span[i];

                while (matchedLength > 0 && current != oldString[matchedLength])
                {
                    int fallbackLength = prefixTable[matchedLength - 1];
                    outputBuffer.Append(oldString.AsSpan(0, matchedLength - fallbackLength));
                    matchedLength = fallbackLength;
                    if (outputBuffer.Length >= OutputBufferFlushThreshold)
                    {
                        await FlushOutputBufferAsync(writer, outputBuffer, cancellationToken);
                    }
                }

                if (current == oldString[matchedLength])
                {
                    matchedLength++;
                    if (matchedLength == oldString.Length)
                    {
                        matches.Add(new(sourceCharacterIndex - oldString.Length + 1));
                        if (matches.Count > 1 && !replaceAll)
                        {
                            string message = $"""
                                Found multiple matches of the string to replace, but replaceAll is false.
                                To replace all occurrences, set replaceAll to true.
                                To replace only one occurrence, please provide more context to uniquely identify the instance.
                                String: {oldString}
                                """;
                            throw new InvalidOperationException(message);
                        }

                        await AppendReplacementAsync(writer, outputBuffer, newString, cancellationToken);
                        matchedLength = 0;
                    }

                    sourceCharacterIndex++;
                    continue;
                }

                outputBuffer.Append(current);
                if (outputBuffer.Length >= OutputBufferFlushThreshold)
                {
                    await FlushOutputBufferAsync(writer, outputBuffer, cancellationToken);
                }

                sourceCharacterIndex++;
            }
        }

        outputBuffer.Append(oldString.AsSpan(0, matchedLength));
        await FlushOutputBufferAsync(writer, outputBuffer, cancellationToken);
        await writer.FlushAsync(cancellationToken);

        return matches;
    }

    private static async Task AppendReplacementAsync(StreamWriter writer, StringBuilder outputBuffer, string replacement, CancellationToken cancellationToken)
    {
        if (replacement.Length is 0)
        {
            return;
        }

        if (replacement.Length >= OutputBufferFlushThreshold)
        {
            await FlushOutputBufferAsync(writer, outputBuffer, cancellationToken);
#if NET11_0
#error Remove .AsMemory()
#endif
            await writer.WriteAsync(replacement.AsMemory(), cancellationToken);
            return;
        }

        outputBuffer.Append(replacement);
        if (outputBuffer.Length >= OutputBufferFlushThreshold)
        {
            await FlushOutputBufferAsync(writer, outputBuffer, cancellationToken);
        }
    }

    private static async Task FlushOutputBufferAsync(StreamWriter writer, StringBuilder outputBuffer, CancellationToken cancellationToken)
    {
        if (outputBuffer.Length is 0)
        {
            return;
        }

        foreach (ReadOnlyMemory<char> chunk in outputBuffer.GetChunks())
        {
            await writer.WriteAsync(chunk, cancellationToken);
        }

        outputBuffer.Clear();
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

    [DisplayName(Prompt.EditToolName)]
    [Description(Prompt.EditToolDescription)]
    private async Task<string> EditAsync(
        [Description("The absolute file to the file to modify")] string filePath,
        [Description("The text to replace")] string oldString,
        [Description("The text to replace it with (must be different from oldString)")] string newString,
        [Description("Replace all occurrences of oldString (default false)")] bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        if (!Context.TryResolveWorkspaceFile(filePath, out AgentWorkspaceFile? file, out string? message))
        {
            return message;
        }

        ValidationResult validationResult = await ValidateInputAsync(file, oldString, newString, cancellationToken);
        if (!validationResult.Result)
        {
            return validationResult.Message;
        }

        // TODO: for empty oldString, we should check if the file exists and is empty, and if so, allow the replacement to create a new file with newString.
        ArgumentException.ThrowIfNullOrEmpty(oldString, "oldString cannot be empty", nameof(oldString));

        AgentWorkspaceEditToolResult.TryAdd(CallId, await ReplaceAsync(file, oldString, newString, replaceAll, cancellationToken));
        Context.InvalidateFileRead(file);

        return replaceAll
            ? $"The file {file.FullPath} has been updated. All occurrences were successfully replaced."
            : $"The file {file.FullPath} has been updated successfully.";
    }
}
