namespace Snap.Nicole.Core.Text;

internal readonly struct TextSpan : IEquatable<TextSpan>, IComparable<TextSpan>
{
    public TextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(start + length, start);

        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int End { get => Start + Length; }

    public int Length { get; }

    public bool IsEmpty { get => Length is 0; }

    public static bool operator ==(TextSpan left, TextSpan right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TextSpan left, TextSpan right)
    {
        return !left.Equals(right);
    }

    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        return new TextSpan(start, end - start);
    }

    public bool Contains(int position)
    {
        return unchecked((uint)(position - Start) < (uint)Length);
    }

    public bool Contains(TextSpan span)
    {
        return span.Start >= Start && span.End <= End;
    }

    public bool OverlapsWith(TextSpan span)
    {
        int overlapStart = Math.Max(Start, span.Start);
        int overlapEnd = Math.Min(End, span.End);

        return overlapStart < overlapEnd;
    }

    public TextSpan? Overlap(TextSpan span)
    {
        int overlapStart = Math.Max(Start, span.Start);
        int overlapEnd = Math.Min(End, span.End);

        return overlapStart < overlapEnd ? TextSpan.FromBounds(overlapStart, overlapEnd) : null;
    }

    public bool IntersectsWith(TextSpan span)
    {
        return span.Start <= End && span.End >= Start;
    }

    public bool IntersectsWith(int position)
    {
        return unchecked((uint)(position - Start) <= (uint)Length);
    }

    public TextSpan? Intersection(TextSpan span)
    {
        int intersectStart = Math.Max(Start, span.Start);
        int intersectEnd = Math.Min(End, span.End);

        // TextSpan.FromBounds
        return intersectStart < intersectEnd ? TextSpan.FromBounds(intersectStart, intersectEnd) : null;
    }

    public bool Equals(TextSpan other)
    {
        return Start == other.Start && Length == other.Length;
    }

    public override bool Equals(object? obj)
    {
        return obj is TextSpan other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Start, Length);
    }

    public override string ToString()
    {
        return $"[{Start}..{End})";
    }

    public int CompareTo(TextSpan other)
    {
        int diff = Start - other.Start;
        if (diff is not 0)
        {
            return diff;
        }

        return Length - other.Length;
    }
}