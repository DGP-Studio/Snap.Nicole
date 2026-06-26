using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Core.IO.StreamingText;

internal static class StreamingText
{
    private const int OutputBufferFlushThreshold = 4096;
    private const int StreamBufferSize = 4096;

    public static async Task<IReadOnlyList<StreamingTextMatch>> ReplaceAsync(TextReader reader, StreamWriter writer, string oldString, string newString, bool replaceAll, CancellationToken cancellationToken)
    {
        List<StreamingTextMatch> matches = [];

        using IMemoryOwner<char> readBufferOwner = MemoryPool<char>.Shared.Rent(StreamBufferSize);
        Memory<char> readBuffer = readBufferOwner.Memory[..StreamBufferSize];

        StringBuilder outputBuffer = new(OutputBufferFlushThreshold);

        int matchedLength = 0;
        long sourceCharacterIndex = 0;
        int[] prefixTable = CreateKmpPrefixTable(oldString);

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
                            throw new StreamingTextMultipleMatchesException();
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

    private static int[] CreateKmpPrefixTable(ReadOnlySpan<char> value)
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
}
