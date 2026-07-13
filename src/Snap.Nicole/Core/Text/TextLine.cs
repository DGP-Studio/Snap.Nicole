namespace Snap.Nicole.Core.Text;

internal readonly struct TextLine
{
    private readonly Text sourceText;
    private readonly TextSpan span;
    private readonly LinePositionSpan positionSpan;

    public TextLine(Text sourceText, int lineIndex, int startIndex, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineIndex);

        TextSpan span = new(startIndex, length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(span.End, sourceText.Length);

        this.sourceText = sourceText;
        this.span = span;
        positionSpan = new(new(lineIndex, 0), new(lineIndex, length));
        LineIndex = lineIndex;
    }

    public Text SourceText { get => sourceText; }

    public int LineIndex { get; }

    public int LineNumber { get => LineIndex + 1; }

    public TextSpan Span { get => span; }

    public LinePositionSpan PositionSpan { get => positionSpan; }

    public int StartIndex { get => span.Start; }

    public int EndIndex { get => span.End; }

    public int Length { get => span.Length; }

    public string Value { get => sourceText.Value.Substring(span.Start, span.Length); }

    public override string ToString()
    {
        return Value;
    }
}
