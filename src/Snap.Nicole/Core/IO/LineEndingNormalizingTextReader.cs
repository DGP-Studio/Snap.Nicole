using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Snap.Nicole.Core.Text;

namespace Snap.Nicole.Core.IO;

internal sealed class LineEndingNormalizingTextReader : TextReader
{
    private const int BufferSize = 4096;
    private const int EndOfStream = -1;
    private const int CR = '\r';
    private const int LF = '\n';

    private readonly TextReader sourceReader;

    private IMemoryOwner<char>? sourceBufferOwner;
    private Memory<char> sourceBuffer;
    private int sourceBufferOffset;
    private int sourceBufferLength;
    private int pendingSourceCharacter = EndOfStream;
    private int bufferedCharacter = EndOfStream;

    public LineEndingNormalizingTextReader(TextReader sourceReader)
    {
        ArgumentNullException.ThrowIfNull(sourceReader);

        this.sourceReader = sourceReader;
        sourceBufferOwner = MemoryPool<char>.Shared.Rent(BufferSize);
        sourceBuffer = sourceBufferOwner.Memory[..BufferSize];
    }

    public override int Peek()
    {
        if (bufferedCharacter is EndOfStream)
        {
            bufferedCharacter = ReadNormalizedCharacter();
        }

        return bufferedCharacter;
    }

    public override int Read()
    {
        return ReadCharacter();
    }

    public override int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(index, count));
    }

    public override int Read(Span<char> buffer)
    {
        int readCount = 0;
        while (readCount < buffer.Length)
        {
            int current = ReadCharacter();
            if (current is EndOfStream)
            {
                break;
            }

            buffer[readCount] = (char)current;
            readCount++;
        }

        return readCount;
    }

    public override string? ReadLine()
    {
        StringBuilder? builder = null;
        while (true)
        {
            int current = ReadCharacter();
            if (current is LF)
            {
                return builder?.ToString() ?? string.Empty;
            }

            if (current is EndOfStream)
            {
                return builder?.ToString();
            }

            builder ??= new();
            builder.Append((char)current);
        }
    }

    public override Task<int> ReadAsync(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadAsync(buffer.AsMemory(index, count)).AsTask();
    }

    public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int readCount = 0;
        while (readCount < buffer.Length)
        {
            int current = await ReadCharacterAsync(cancellationToken);
            if (current is EndOfStream)
            {
                break;
            }

            buffer.Span[readCount] = (char)current;
            readCount++;
        }

        return readCount;
    }

    public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        StringBuilder? builder = null;
        while (true)
        {
            int current = await ReadCharacterAsync(cancellationToken);
            if (current is LF)
            {
                return builder?.ToString() ?? string.Empty;
            }

            if (current is EndOfStream)
            {
                return builder?.ToString();
            }

            builder ??= new();
            builder.Append((char)current);
        }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                sourceBufferOwner?.Dispose();
                sourceBufferOwner = null;
                sourceReader.Dispose();
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    private static bool IsSingleCharacterLineEnding(int value)
    {
        return value is not CR && LineEnding.IsNewLine((char)value);
    }

    private int ReadCharacter()
    {
        if (bufferedCharacter is not EndOfStream)
        {
            return Adaptive.Exchange(ref bufferedCharacter, EndOfStream);
        }

        return ReadNormalizedCharacter();
    }

    private int ReadNormalizedCharacter()
    {
        int current = ReadSourceCharacter();
        if (current is CR)
        {
            int next = ReadSourceCharacter();
            if (next is not LF)
            {
                pendingSourceCharacter = next;
            }

            return LF;
        }

        return IsSingleCharacterLineEnding(current) ? LF : current;
    }

    private int ReadSourceCharacter()
    {
        if (pendingSourceCharacter is not EndOfStream)
        {
            return Adaptive.Exchange(ref pendingSourceCharacter, EndOfStream);
        }

        if (sourceBufferOffset >= sourceBufferLength)
        {
            sourceBufferLength = sourceReader.Read(sourceBuffer.Span);
            sourceBufferOffset = 0;
            if (sourceBufferLength is 0)
            {
                return EndOfStream;
            }
        }

        return sourceBuffer.Span[sourceBufferOffset++];
    }

    private async ValueTask<int> ReadCharacterAsync(CancellationToken cancellationToken)
    {
        if (bufferedCharacter is not EndOfStream)
        {
            return Adaptive.Exchange(ref bufferedCharacter, EndOfStream);
        }

        return await ReadNormalizedCharacterAsync(cancellationToken);
    }

    private async ValueTask<int> ReadNormalizedCharacterAsync(CancellationToken cancellationToken)
    {
        int current = await ReadSourceCharacterAsync(cancellationToken);
        if (current is CR)
        {
            int next = await ReadSourceCharacterAsync(cancellationToken);
            if (next is not LF)
            {
                pendingSourceCharacter = next;
            }

            return LF;
        }

        return IsSingleCharacterLineEnding(current) ? LF : current;
    }

    private async ValueTask<int> ReadSourceCharacterAsync(CancellationToken cancellationToken)
    {
        if (pendingSourceCharacter is not EndOfStream)
        {
            return Adaptive.Exchange(ref pendingSourceCharacter, EndOfStream);
        }

        if (sourceBufferOffset >= sourceBufferLength)
        {
            sourceBufferLength = await sourceReader.ReadAsync(sourceBuffer, cancellationToken);
            sourceBufferOffset = 0;
            if (sourceBufferLength is 0)
            {
                return EndOfStream;
            }
        }

        return sourceBuffer.Span[sourceBufferOffset++];
    }
}
