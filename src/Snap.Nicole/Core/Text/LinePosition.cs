namespace Snap.Nicole.Core.Text;

internal readonly struct LinePosition : IEquatable<LinePosition>, IComparable<LinePosition>
{
    public static LinePosition Zero => default;

    private readonly int line;
    private readonly int character;

    public LinePosition(int line, int character)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(character);

        this.line = line;
        this.character = character;
    }

    public int Line { get => line; }

    public int Character { get => character; }

    public static bool operator ==(LinePosition left, LinePosition right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LinePosition left, LinePosition right)
    {
        return !left.Equals(right);
    }

    public bool Equals(LinePosition other)
    {
        return other.Line == Line && other.Character == Character;
    }

    public override bool Equals(object? obj)
    {
        return obj is LinePosition position && Equals(position);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Line, Character);
    }

    public override string ToString()
    {
        return $"{Line},{Character}";
    }

    public int CompareTo(LinePosition other)
    {
        int result = line.CompareTo(other.line);
        return (result is not 0) ? result : character.CompareTo(other.Character);
    }

    public static bool operator >(LinePosition left, LinePosition right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(LinePosition left, LinePosition right)
    {
        return left.CompareTo(right) >= 0;
    }

    public static bool operator <(LinePosition left, LinePosition right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(LinePosition left, LinePosition right)
    {
        return left.CompareTo(right) <= 0;
    }
}
