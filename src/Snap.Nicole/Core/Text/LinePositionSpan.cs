namespace Snap.Nicole.Core.Text;

internal readonly struct LinePositionSpan : IEquatable<LinePositionSpan>
{
    private readonly LinePosition start;
    private readonly LinePosition end;

    public LinePositionSpan(LinePosition start, LinePosition end)
    {
        if (end < start)
        {
            throw new ArgumentException("End position must be greater than or equal to start position.", nameof(end));
        }

        this.start = start;
        this.end = end;
    }

    public LinePosition Start { get => start; }

    public LinePosition End { get => end; }

    public static bool operator ==(LinePositionSpan left, LinePositionSpan right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LinePositionSpan left, LinePositionSpan right)
    {
        return !left.Equals(right);
    }

    public override bool Equals(object? obj)
    {
        return obj is LinePositionSpan span && Equals(span);
    }

    public bool Equals(LinePositionSpan other)
    {
        return start.Equals(other.start) && end.Equals(other.end);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(start.GetHashCode(), end.GetHashCode());
    }

    public override string ToString()
    {
        return $"({start})-({end})";
    }
}
