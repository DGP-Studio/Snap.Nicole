using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using Snap.Nicole.Core.IO;
using System.Buffers;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFileAccessEditTool(AgentWorkspaceFileAccessContext context)
    : AgentWorkspaceFileAccessToolComponent(context)
{
    private const int StreamBufferSize = 8192;
    private const int OutputBufferFlushThreshold = 16384;

    public override AITool Tool { get => field ??= AIFunctionFactory.Create(EditAsync).AsApprovalRequired(); }

    [DisplayName(AgentWorkspaceFileAccessToolNames.Edit)]
    [Description("""
        Performs exact string replacement in a file.

        - You must Read the file in this conversation before editing, or the call will fail.
        - `oldString` must match the file exactly, including indentation, and be unique — the edit fails otherwise. Strip the Read line prefix (line number + tab) before matching.
        - `replaceAll: true` replaces every occurrence instead.
        """)]
    private async Task<string> EditAsync(
        [Description("The absolute path to the file to modify")] string filePath,
        [Description("The text to replace it with (must be different from oldString)")] string newString,
        [Description("The text to replace")] string oldString,
        [Description("Replace all occurrences of oldString (default false)")] bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        AgentWorkspacePath path = Context.ResolveAbsoluteFilePath(filePath);
        if (!File.Exists(path.FullPath))
        {
            return $"File '{path.DisplayPath}' not found.";
        }

        InvalidOperationException.ThrowIfNot(Context.WasFileRead(path.FullPath), $"File '{path.DisplayPath}' must be read with {AgentWorkspaceFileAccessToolNames.Read} tool before editing.");
        ArgumentException.ThrowIfNullOrEmpty(oldString, "oldString cannot be empty", nameof(oldString));
        ArgumentException.ThrowIf(string.Equals(oldString, newString, StringComparison.Ordinal), "newString must be different from oldString.", nameof(newString));

        int replacementCount = await EditCoreAsync(path.FullPath, oldString, newString, replaceAll, path.DisplayPath, cancellationToken);
        return $"File '{path.DisplayPath}' edited. Replaced {replacementCount} occurrence(s).";
    }

    private static async Task<int> EditCoreAsync(string sourcePath, string oldString, string newString, bool replaceAll, string displayPath, CancellationToken cancellationToken)
    {
        int[] prefixTable = CreateKMPPrefixTable(oldString);
        using IMemoryOwner<char> readBufferOwner = MemoryPool<char>.Shared.Rent(StreamBufferSize);
        Memory<char> readBuffer = readBufferOwner.Memory[..StreamBufferSize];
        StringBuilder outputBuffer = new(OutputBufferFlushThreshold);
        int matchedLength = 0;
        int replacementCount = 0;

        using TemporaryFileStream temporaryStream = new(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous);
        using (StreamReader reader = new(new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        using (StreamWriter writer = new(temporaryStream, Encoding.UTF8, StreamBufferSize, leaveOpen: true))
        {
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
                            replacementCount++;
                            if (!replaceAll && replacementCount > 1)
                            {
                                throw new InvalidOperationException($"oldString is not unique in '{displayPath}'. Pass replaceAll true to replace every occurrence.");
                            }

                            await AppendReplacementAsync(writer, outputBuffer, newString, cancellationToken);
                            matchedLength = 0;
                        }

                        continue;
                    }

                    outputBuffer.Append(current);
                    if (outputBuffer.Length >= OutputBufferFlushThreshold)
                    {
                        await FlushOutputBufferAsync(writer, outputBuffer, cancellationToken);
                    }
                }
            }

            outputBuffer.Append(oldString.AsSpan(0, matchedLength));
            await FlushOutputBufferAsync(writer, outputBuffer, cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }

        if (replacementCount is 0)
        {
            throw new InvalidOperationException($"oldString was not found in '{displayPath}'.");
        }

        temporaryStream.Commit();
        return replacementCount;
    }

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

    private static async Task AppendReplacementAsync(StreamWriter writer, StringBuilder outputBuffer, string replacement, CancellationToken cancellationToken)
    {
        if (replacement.Length is 0)
        {
            return;
        }

        if (replacement.Length >= OutputBufferFlushThreshold)
        {
            await FlushOutputBufferAsync(writer, outputBuffer, cancellationToken);
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
}
