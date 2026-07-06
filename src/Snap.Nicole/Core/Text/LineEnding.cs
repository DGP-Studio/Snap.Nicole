using System.Linq;

namespace Snap.Nicole.Core.Text;

internal static class LineEnding
{
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
        for (int i = 0; i < value.Length; i++)
        {
            if (GetKind(value, ref i) is not { } currentKind)
            {
                continue;
            }

            int kindIndex = (int)currentKind;
            if (counts[kindIndex] is 0)
            {
                firstIndexes[kindIndex] = currentLineEndingIndex;
            }

            counts[kindIndex]++;
            currentLineEndingIndex++;
        }

        if (currentLineEndingIndex is 0)
        {
            return null;
        }

        LineEndingKind dominantKind = (LineEndingKind)counts
            .Zip(firstIndexes, static (count, firstIndex) => (Count: count, FirstIndex: firstIndex))
            .Select(static (item, kindIndex) => (KindIndex: kindIndex, item.Count, item.FirstIndex))
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.FirstIndex)
            .First()
            .KindIndex;

        return dominantKind switch
        {
            LineEndingKind.CarriageReturnLineFeed => "\r\n",
            LineEndingKind.CarriageReturn => "\r",
            LineEndingKind.LineFeed => "\n",
            LineEndingKind.FormFeed => "\f",
            LineEndingKind.NextLine => "\u0085",
            LineEndingKind.LineSeparator => "\u2028",
            LineEndingKind.ParagraphSeparator => "\u2029",
            _ => throw new ArgumentOutOfRangeException(nameof(dominantKind)),
        };
    }

    private static LineEndingKind? GetKind(ReadOnlySpan<char> value, ref int index)
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
                return null;
        }
    }
}
