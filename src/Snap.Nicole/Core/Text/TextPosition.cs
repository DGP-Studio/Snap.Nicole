namespace Snap.Nicole.Core.Text;

internal readonly struct TextPosition
{
    private const char LF = '\n';

    public TextPosition(int line, int column)
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }

    public static int GetInclusiveEndLine(TextPosition startPosition, TextPosition endExclusivePosition)
    {
        if (endExclusivePosition.Column is 0)
        {
            return Math.Max(startPosition.Line, endExclusivePosition.Line - 1);
        }

        return endExclusivePosition.Line;
    }

    public TextPosition Advance(char value)
    {
        if (value is LF)
        {
            return new(Line + 1, 0);
        }

        return new(Line, Column + 1);
    }

    public TextPosition Advance(NormalizedString value)
    {
        ReadOnlySpan<char> span = value.Value.AsSpan();

        int lineEndingIndex = span.IndexOf(LF);
        if (lineEndingIndex < 0)
        {
            return new(Line, Column + span.Length);
        }

        int line = Line + 1;
        int nextSegmentStart = lineEndingIndex + 1;

        while ((lineEndingIndex = span[nextSegmentStart..].IndexOf(LF)) >= 0)
        {
            line++;
            nextSegmentStart += lineEndingIndex + 1;
        }

        return new(line, span.Length - nextSegmentStart);
    }
}
