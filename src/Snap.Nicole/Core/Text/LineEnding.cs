using System.Buffers;

namespace Snap.Nicole.Core.Text;

internal static class LineEnding
{
    private static readonly SearchValues<char> NewLineChars = SearchValues.Create("\r\n\f\u0085\u2028\u2029");

    private enum LineEndingKind
    {
        CarriageReturnLineFeed,
        CarriageReturn,
        LineFeed,
        FormFeed,
        NextLine,
        LineSeparator,
        ParagraphSeparator,
        Count,
    }

    public static string? GetDominantLineEnding(ReadOnlySpan<char> value)
    {
        int[] counts = new int[(int)LineEndingKind.Count];
        int[] firstIndexes = new int[(int)LineEndingKind.Count];
        Array.Fill(firstIndexes, int.MaxValue);

        int currentLineEndingIndex = 0;
        ReadOnlySpan<char> remainingValue = value;
        while (true)
        {
            int index = IndexOfNewLine(remainingValue);
            if (index < 0)
            {
                break;
            }

            LineEndingKind currentKind = GetKind(remainingValue, ref index);
            int kindIndex = (int)currentKind;
            if (counts[kindIndex] is 0)
            {
                firstIndexes[kindIndex] = currentLineEndingIndex;
            }

            counts[kindIndex]++;
            currentLineEndingIndex++;
            remainingValue = remainingValue[(index + 1)..];
        }

        if (currentLineEndingIndex is 0)
        {
            return null;
        }

        int dominantKindIndex = 0;
        for (int kindIndex = 1; kindIndex < counts.Length; kindIndex++)
        {
            if (counts[kindIndex] > counts[dominantKindIndex]
                || (counts[kindIndex] == counts[dominantKindIndex] && firstIndexes[kindIndex] < firstIndexes[dominantKindIndex]))
            {
                dominantKindIndex = kindIndex;
            }
        }

        LineEndingKind dominantKind = (LineEndingKind)dominantKindIndex;
        return dominantKind switch
        {
            LineEndingKind.CarriageReturnLineFeed => "\r\n",
            LineEndingKind.CarriageReturn => "\r",
            LineEndingKind.LineFeed => "\n",
            LineEndingKind.FormFeed => "\f",
            LineEndingKind.NextLine => "\u0085",
            LineEndingKind.LineSeparator => "\u2028",
            LineEndingKind.ParagraphSeparator => "\u2029",
            _ => throw new InvalidOperationException($"Unexpected LineEndingKind: {dominantKind}"),
        };
    }

    internal static int IndexOfNewLine(ReadOnlySpan<char> value)
    {
        return value.IndexOfAny(NewLineChars);
    }

    internal static bool IsNewLine(char value)
    {
        return NewLineChars.Contains(value);
    }

    private static LineEndingKind GetKind(ReadOnlySpan<char> value, ref int index)
    {
        switch (value[index])
        {
            case '\r':
                if (index + 1 < value.Length && value[index + 1] is '\n')
                {
                    index++;
                    return LineEndingKind.CarriageReturnLineFeed;
                }

                return LineEndingKind.CarriageReturn;
            case '\n':
                return LineEndingKind.LineFeed;
            case '\f':
                return LineEndingKind.FormFeed;
            case '\u0085':
                return LineEndingKind.NextLine;
            case '\u2028':
                return LineEndingKind.LineSeparator;
            case '\u2029':
                return LineEndingKind.ParagraphSeparator;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
