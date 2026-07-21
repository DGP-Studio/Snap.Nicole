using Snap.Nicole.Core.Collections.Generic;
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

    public static TextLineCollection FromText(string value, bool trimTrailingEmptyLine = false)
    {
        return FromText(new Text(value), trimTrailingEmptyLine);
    }

    public static TextLineCollection FromText(Text value, bool trimTrailingEmptyLine = false)
    {
        if (trimTrailingEmptyLine && value.IsEmpty)
        {
            return Empty;
        }

        ArrayBuilder<TextLine> lines = new();
        int lineNumber = 0;
        int lineStartIndex = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value.Value[i] is not LineEnding.LF)
            {
                continue;
            }

            lines.Add(new(value, lineNumber, lineStartIndex, i - lineStartIndex));
            lineNumber++;
            lineStartIndex = i + 1;
        }

        if (!trimTrailingEmptyLine || lineStartIndex < value.Length)
        {
            lines.Add(new(value, lineNumber, lineStartIndex, value.Length - lineStartIndex));
        }

        return new(lines.ToArrayAndClear());
    }

    public int IndexOf(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        if (lines.Length is 0 || position < lines[0].Start || position > lines[^1].End)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        int index = lines.AsSpan().BinarySearch(new TextLinePositionComparable(position));
        return index >= 0 ? index : ~index - 1;
    }

    public TextLine GetLineFromPosition(int position)
    {
        return lines[IndexOf(position)];
    }

    public LinePosition GetLinePosition(int position)
    {
        TextLine line = GetLineFromPosition(position);
        return new(line.LineNumber, position - line.Start);
    }

    public LinePositionSpan GetLinePositionSpan(TextSpan span)
    {
        return new(GetLinePosition(span.Start), GetLinePosition(span.End));
    }

    public int GetPosition(LinePosition position)
    {
        if (lines.Length is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        int relativeLine = position.Line - lines[0].LineNumber;
        if (relativeLine < 0 || relativeLine >= lines.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        TextLine line = lines[relativeLine];
        if (position.Character > line.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return line.Start + position.Character;
    }

    public TextSpan GetTextSpan(LinePositionSpan span)
    {
        return TextSpan.FromBounds(GetPosition(span.Start), GetPosition(span.End));
    }

    public TextLineCollection Slice(int start, int length)
    {
        return new(lines.AsSpan(start, length).ToArray());
    }

    public IEnumerator<TextLine> GetEnumerator()
    {
        return ((IEnumerable<TextLine>)lines).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private sealed class TextLinePositionComparable(int position) : IComparable<TextLine>
    {
        private readonly int position = position;

        public int CompareTo(TextLine other)
        {
            return position.CompareTo(other.Start);
        }
    }
}
