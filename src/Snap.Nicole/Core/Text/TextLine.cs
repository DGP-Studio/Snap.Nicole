namespace Snap.Nicole.Core.Text;

internal readonly struct TextLine
{
    private readonly Text text;
    private readonly TextSpan span;

    public TextLine(Text text, int lineNumber, int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineNumber);

        TextSpan span = new(start, length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(span.End, text.Length);

        this.text = text;
        this.span = span;
        LineNumber = lineNumber;
    }

    public Text Text { get => text; }

    public int LineNumber { get; }

    public int Start { get => span.Start; }

    public int End { get => span.End; }

    public TextSpan Span { get => span; }

    public int Length { get => span.Length; }

    public static bool operator ==(TextLine left, TextLine right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TextLine left, TextLine right)
    {
        return !left.Equals(right);
    }

    public bool Equals(TextLine other)
    {
        // Same text and same span means same line.
        return other.text == text && other.span == span;
    }

    public override bool Equals(object? obj)
    {
        return obj is TextLine line && Equals(line);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(text.GetHashCode(), span.GetHashCode());
    }

    public override string ToString()
    {
        if (text.IsEmpty)
        {
            return string.Empty;
        }

        return text.ToString(span);
    }
}
