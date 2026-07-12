using System.Collections;
using System.Collections.Generic;

namespace Snap.Nicole.Core.Text;

internal sealed class TextLineCollection : IList<string>, IReadOnlyList<string>
{
    private const char LF = '\n';

    private readonly List<string> lines;

    private int[]? lineStartOffsets;
    private int textLength;

    public TextLineCollection()
    {
        lines = [];
    }

    public TextLineCollection(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        this.lines = [];
        foreach (string line in lines)
        {
            Add(line);
        }
    }

    public int Count { get => lines.Count; }

    public bool IsReadOnly { get => false; }

    public string this[int index]
    {
        get => lines[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            lines[index] = value;
            InvalidateLineStartOffsets();
        }
    }

    public static TextLineCollection FromText(string value)
    {
        return FromText(new NormalizedString(value));
    }

    public static TextLineCollection FromText(NormalizedString value)
    {
        string[] splitLines = value.Value.Split(LF);
        int lineCount = splitLines.Length > 0 && splitLines[^1].Length is 0 ? splitLines.Length - 1 : splitLines.Length;

        TextLineCollection lines = [];
        for (int i = 0; i < lineCount; i++)
        {
            lines.Add(splitLines[i]);
        }

        return lines;
    }

    public void Add(string item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lines.Add(item);
        InvalidateLineStartOffsets();
    }

    public void Clear()
    {
        lines.Clear();
        InvalidateLineStartOffsets();
    }

    public bool Contains(string item)
    {
        return lines.Contains(item);
    }

    public void CopyTo(string[] array, int arrayIndex)
    {
        lines.CopyTo(array, arrayIndex);
    }

    public IEnumerator<string> GetEnumerator()
    {
        return lines.GetEnumerator();
    }

    public int GetTextOffset(int startLine, TextPosition position)
    {
        int relativeLine = position.Line - startLine;
        if (relativeLine < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position line cannot be before the collection start line.");
        }

        if (position.Column < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position column cannot be negative.");
        }

        if (relativeLine >= lines.Count)
        {
            return GetTextLength();
        }

        string line = lines[relativeLine];
        if (position.Column > line.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position column cannot be beyond the line length.");
        }

        return GetLineStartOffsets()[relativeLine] + position.Column;
    }

    public int IndexOf(string item)
    {
        return lines.IndexOf(item);
    }

    public void Insert(int index, string item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lines.Insert(index, item);
        InvalidateLineStartOffsets();
    }

    public bool Remove(string item)
    {
        bool removed = lines.Remove(item);
        if (removed)
        {
            InvalidateLineStartOffsets();
        }

        return removed;
    }

    public void RemoveAt(int index)
    {
        lines.RemoveAt(index);
        InvalidateLineStartOffsets();
    }

    public string ToText()
    {
        return string.Join(LF, lines);
    }

    public TextLineCollection WithoutTrailingEmptyLine()
    {
        if (lines.Count is 0 || lines[^1].Length is not 0)
        {
            return this;
        }

        TextLineCollection result = [];
        for (int i = 0; i < lines.Count - 1; i++)
        {
            result.Add(lines[i]);
        }

        return result;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private int GetTextLength()
    {
        EnsureLineStartOffsets();
        return textLength;
    }

    private int[] GetLineStartOffsets()
    {
        return EnsureLineStartOffsets();
    }

    private int[] EnsureLineStartOffsets()
    {
        if (lineStartOffsets is { } cachedLineStartOffsets)
        {
            return cachedLineStartOffsets;
        }

        int[] offsets = new int[lines.Count];
        int offset = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            offsets[i] = offset;
            offset += lines[i].Length + 1;
        }

        textLength = lines.Count > 0 ? offset - 1 : 0;
        lineStartOffsets = offsets;
        return offsets;
    }

    private void InvalidateLineStartOffsets()
    {
        lineStartOffsets = null;
        textLength = 0;
    }
}
