using System.Collections;
using System.Collections.Generic;

namespace Snap.Nicole.Core.Text;

internal sealed class TextLineCollection : IReadOnlyList<string>
{
    private const char LF = '\n';

    private readonly string[] lines;

    private int[]? lineStartOffsets;
    private int textLength;

    public TextLineCollection()
    {
        lines = [];
    }

    private TextLineCollection(string[] lines)
    {
        this.lines = lines;
    }

    public int Count { get => lines.Length; }

    public string this[int index] { get => lines[index]; }

    public static TextLineCollection FromText(string value)
    {
        return FromText(new Text(value));
    }

    public static TextLineCollection FromText(Text value)
    {
        string[] splitLines = value.Value.Split(LF);
        int lineCount = splitLines.Length > 0 && splitLines[^1].Length is 0 ? splitLines.Length - 1 : splitLines.Length;

        // TODO: optimize lineCount & splitLines reference to avoid unnecessary array copy when lineCount is equal to splitLines.Length
        return [with(splitLines[..lineCount])];
    }

    public TextLineCollection Add(string item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return [with([.. lines, item])];
    }

    public IEnumerator<string> GetEnumerator()
    {
        return ((IEnumerable<string>)lines).GetEnumerator();
    }

    public int GetTextOffset(int startLine, TextPosition position)
    {
        int relativeLine = position.Line - startLine;
        if (relativeLine < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position line cannot be before the collection start line.");
        }

        if (position.Column < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position column cannot be negative.");
        }

        if (relativeLine >= lines.Length)
        {
            return GetTextLength();
        }

        string line = lines[relativeLine];
        if (position.Column > line.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position column cannot be beyond the line length.");
        }

        return GetLineStartOffsets()[relativeLine] + position.Column;
    }

    public string ToText()
    {
        return string.Join(LF, lines);
    }

    public TextLineCollection WithoutTrailingEmptyLine()
    {
        if (lines.Length is 0 || lines[^1].Length is not 0)
        {
            return this;
        }

        return new(lines[..^1]);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private int GetTextLength()
    {
        EnsureLineStartOffsets();
        return textLength;
    }

    private int[] GetLineStartOffsets()
    {
        return EnsureLineStartOffsets();
    }

    private int[] EnsureLineStartOffsets()
    {
        if (lineStartOffsets is { } cachedLineStartOffsets)
        {
            return cachedLineStartOffsets;
        }

        int[] offsets = new int[lines.Length];
        int offset = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            offsets[i] = offset;
            offset += lines[i].Length + 1;
        }

        textLength = lines.Length > 0 ? offset - 1 : 0;
        lineStartOffsets = offsets;
        return offsets;
    }

}
