using System.Text;

namespace Snap.Nicole.Core.Text;

internal readonly struct Text : IEquatable<Text>
{
    public Text(string value)
    {
        Value = NormalizeLineEndings(value, out int lineEndingCount);
        LineEndingCount = lineEndingCount;
    }

    public string Value { get; }

    public int Length { get => Value.Length; }

    public bool IsEmpty { get => Value.Length is 0; }

    public int LineEndingCount { get; }

    public static bool operator ==(Text left, Text right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Text left, Text right)
    {
        return !left.Equals(right);
    }

    public int IndexOf(Text value, int startIndex, StringComparison comparisonType)
    {
        return Value.IndexOf(value.Value, startIndex, comparisonType);
    }

    public string Replace(Text oldValue, Text newValue)
    {
        return Value.Replace(oldValue.Value, newValue.Value);
    }

    public bool Equals(Text other)
    {
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is Text text && Equals(text);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public string ToString(TextSpan span)
    {
        return Value.Substring(span.Start, span.Length);
    }

    private static string NormalizeLineEndings(string value, out int lineEndingCount)
    {
        StringBuilder? builder = null;
        int segmentStart = 0;
        lineEndingCount = 0;

        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (!LineEnding.IsNewLine(current))
            {
                continue;
            }

            lineEndingCount++;
            if (builder is null && current is '\n')
            {
                continue;
            }

            builder ??= new(value.Length);
            builder.Append(value.AsSpan(segmentStart, i - segmentStart));
            builder.Append('\n');

            if (current is '\r' && i + 1 < value.Length && value[i + 1] is '\n')
            {
                i++;
            }

            segmentStart = i + 1;
        }

        if (builder is null)
        {
            return value;
        }

        builder.Append(value.AsSpan(segmentStart));
        return builder.ToString();
    }
}
