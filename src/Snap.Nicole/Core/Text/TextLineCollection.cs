using System.Collections;
using System.Collections.Generic;

namespace Snap.Nicole.Core.Text;

internal sealed class TextLineCollection : IReadOnlyList<TextLine>
{
    private readonly TextLine[] lines;

    private TextLineCollection()
    {
        lines = [];
    }

    private TextLineCollection(TextLine[] lines)
    {
        this.lines = lines;
    }

    public static TextLineCollection Empty { get; } = [];

    public int Count { get => lines.Length; }

    public TextLine this[int index] { get => lines[index]; }

    public static TextLineCollection FromText(string value)
    {
        return FromText(new Text(value));
    }

    public static TextLineCollection FromText(Text value)
    {
        if (value.IsEmpty)
        {
            return Empty;
        }

        List<TextLine> lines = [];
        int lineIndex = 0;
        int lineStartIndex = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value.Value[i] is not LineEnding.LF)
            {
                continue;
            }

            lines.Add(new(value, lineIndex, lineStartIndex, i - lineStartIndex));
            lineIndex++;
            lineStartIndex = i + 1;
        }

        if (lineStartIndex < value.Length)
        {
            lines.Add(new(value, lineIndex, lineStartIndex, value.Length - lineStartIndex));
        }

        return new([.. lines]);
    }

    public TextLineCollection Slice(int startIndex, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, lines.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, lines.Length - startIndex);

        if (count is 0)
        {
            return Empty;
        }

        if (startIndex is 0 && count == lines.Length)
        {
            return this;
        }

        return new(lines.AsSpan(startIndex, count).ToArray());
    }

    public TextLineCollection WithoutTrailingEmptyLine()
    {
        if (lines.Length is 0 || lines[^1].Length is not 0)
        {
            return this;
        }

        return new(lines[..^1]);
    }

    public IEnumerator<TextLine> GetEnumerator()
    {
        return ((IEnumerable<TextLine>)lines).GetEnumerator();
    }

    public int GetTextOffset(TextPosition position)
    {
        if (lines.Length is 0)
        {
            return 0;
        }

        int relativeLine = position.Line - lines[0].LineIndex;
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
            return lines[^1].EndIndex - lines[0].StartIndex;
        }

        TextLine line = lines[relativeLine];
        if (position.Column > line.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position column cannot be beyond the line length.");
        }

        return line.StartIndex - lines[0].StartIndex + position.Column;
    }

    public string ToText()
    {
        if (lines.Length is 0)
        {
            return string.Empty;
        }

        TextLine firstLine = lines[0];
        int startIndex = firstLine.StartIndex;
        int length = lines[^1].EndIndex - startIndex;

        // Substring returns original instance if startIndex is 0 and length is equal to the string length
        return firstLine.SourceText.Value.Substring(startIndex, length);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
