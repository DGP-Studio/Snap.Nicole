namespace Snap.Nicole.Core.IO.StreamingText;

/// <summary>
/// 0-based index of the match in the stream.
/// </summary>
internal readonly struct StreamingTextMatch
{
    public StreamingTextMatch(long startIndex)
    {
        StartIndex = startIndex;
    }

    public long StartIndex { get; }
}
