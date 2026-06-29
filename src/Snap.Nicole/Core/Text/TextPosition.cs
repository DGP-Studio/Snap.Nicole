namespace Snap.Nicole.Core.Text;

/// <summary>
/// 0-based line and column position in a text file.
/// Line and column numbers are both 0-based, meaning the first line is line 0 and the first column is column 0.
/// </summary>
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

    public static int GetInclusiveEndLine(TextPosition start, TextPosition end)
    {
        if (start.Line > end.Line || (start.Line == end.Line && start.Column > end.Column))
        {
            throw new ArgumentException("Start position must be less than or equal to end position.");
        }

        // If the end position is at the start of a line, we consider the inclusive end line to be the previous line.
        if (end.Column is 0)
        {
            return Math.Max(start.Line, end.Line - 1);
        }

        return end.Line;
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
