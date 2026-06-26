namespace Snap.Nicole.Core.IO.StreamingText;

internal readonly struct StreamingTextMatch
{
    public StreamingTextMatch(long startIndex)
    {
        StartIndex = startIndex;
    }

    public long StartIndex { get; }
}
