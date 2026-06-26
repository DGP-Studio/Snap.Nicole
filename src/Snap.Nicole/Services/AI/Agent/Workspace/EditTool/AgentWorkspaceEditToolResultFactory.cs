using Snap.Nicole.Core.IO;
using Snap.Nicole.Core.IO.StreamingText;
using Snap.Nicole.Core.Text;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal static class AgentWorkspaceEditToolResultFactory
{
    private const char LF = '\n';
    private const char HunkContext = ' ';
    private const char HunkAddition = '+';
    private const char HunkDeletion = '-';
    private const int HunkContextLineCount = 3;
    private const int StreamBufferSize = 4096;

    public static async Task<AgentWorkspaceEditToolResult> CreateAsync(AgentWorkspaceFile file, NormalizedString oldString, NormalizedString newString, IReadOnlyList<StreamingTextMatch> matches, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReplacementLineRange> replacementRanges = await CreateReplacementLineRangesAsync(file, oldString, newString, matches, cancellationToken);
        List<HunkLineRange> hunkLineRanges = CreateHunkLineRanges(replacementRanges);
        List<AgentWorkspaceEditToolHunk> structuredPatch = await CreateStructuredPatchAsync(file.FullPath, hunkLineRanges, newString, cancellationToken);
        CountPatchLines(structuredPatch, out int additions, out int deletions);

        return new()
        {
            FilePath = file.FullPath,
            Additions = additions,
            Deletions = deletions,
            StructuredPatch = structuredPatch,
        };
    }

    private static async Task<IReadOnlyList<ReplacementLineRange>> CreateReplacementLineRangesAsync(AgentWorkspaceFile file, NormalizedString oldString, NormalizedString newString, IReadOnlyList<StreamingTextMatch> matches, CancellationToken cancellationToken)
    {
        if (matches.Count is 0)
        {
            return [];
        }

        List<ReplacementLineRange> ranges = new(matches.Count);
        int replacementLineDelta = newString.LineEndingCount - oldString.LineEndingCount;

        using FileStream stream = file.OpenRead(share: FileShare.ReadWrite | FileShare.Delete);
        using LineEndingNormalizingTextReader reader = new(new StreamReader(stream, Encoding.UTF8WithoutBOM, true));

        using IMemoryOwner<char> bufferOwner = MemoryPool<char>.Shared.Rent(StreamBufferSize);
        Memory<char> buffer = bufferOwner.Memory[..StreamBufferSize];

        int matchIndex = 0;
        TextPosition currentPosition = new(0, 0);
        long currentCharIndex = 0;

        while (matchIndex < matches.Count)
        {
            int readCount = await reader.ReadAsync(buffer, cancellationToken);
            if (readCount is 0)
            {
                break;
            }

            for (int i = 0; i < readCount && matchIndex < matches.Count; i++)
            {
                char current = buffer.Span[i];

                if (currentCharIndex == matches[matchIndex].StartIndex)
                {
                    TextPosition endPosition = currentPosition.Advance(oldString);
                    ranges.Add(new()
                    {
                        OldStartInclusive = currentPosition,
                        OldEndExclusive = endPosition,
                        LineDelta = replacementLineDelta,
                    });

                    matchIndex++;
                }

                currentPosition = currentPosition.Advance(current);
                currentCharIndex++;
            }
        }

        return ranges;
    }

    private static List<HunkLineRange> CreateHunkLineRanges(IReadOnlyList<ReplacementLineRange> replacementRanges)
    {
        List<HunkLineRange> hunkRanges = [];
        int cumulativeLineDelta = 0;
        foreach (ReplacementLineRange replacementRange in replacementRanges)
        {
            int oldStartLine = Math.Max(0, replacementRange.OldStartInclusive.Line - HunkContextLineCount);
            int oldEndLine = TextPosition.GetInclusiveEndLine(replacementRange.OldStartInclusive, replacementRange.OldEndExclusive) + HunkContextLineCount;

            if (hunkRanges.Count > 0 && oldStartLine <= hunkRanges[^1].OldEndLine + 1)
            {
                HunkLineRange currentRange = hunkRanges[^1];
                currentRange.OldEndLine = Math.Max(currentRange.OldEndLine, oldEndLine);
                currentRange.LineDelta += replacementRange.LineDelta;
                currentRange.ReplacementRanges.Add(replacementRange);
            }
            else
            {
                hunkRanges.Add(new()
                {
                    OldStartLine = oldStartLine,
                    OldEndLine = oldEndLine,
                    LineDeltaBefore = cumulativeLineDelta,
                    LineDelta = replacementRange.LineDelta,
                    ReplacementRanges = [replacementRange],
                });
            }

            cumulativeLineDelta += replacementRange.LineDelta;
        }

        return hunkRanges;
    }

    private static async Task<List<AgentWorkspaceEditToolHunk>> CreateStructuredPatchAsync(string sourcePath, List<HunkLineRange> hunkLineRanges, NormalizedString newString, CancellationToken cancellationToken)
    {
        List<AgentWorkspaceEditToolHunk> hunks = new(hunkLineRanges.Count);
        if (hunkLineRanges.Count is 0)
        {
            return hunks;
        }

        using FileStream stream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using TextReader reader = new LineEndingNormalizingTextReader(new StreamReader(stream, Encoding.UTF8WithoutBOM, detectEncodingFromByteOrderMarks: true));

        int currentLineIndex = 0;
        foreach (HunkLineRange hunkLineRange in hunkLineRanges)
        {
            while (currentLineIndex < hunkLineRange.OldStartLine && await reader.ReadLineAsync(cancellationToken) is not null)
            {
                currentLineIndex++;
            }

            List<string> oldLines = [];
            while (currentLineIndex <= hunkLineRange.OldEndLine && await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                oldLines.Add(line);
                currentLineIndex++;
            }

            hunks.Add(CreateHunk(oldLines, hunkLineRange, newString));
        }

        return hunks;
    }

    private static AgentWorkspaceEditToolHunk CreateHunk(List<string> oldLines, HunkLineRange hunkLineRange, NormalizedString newString)
    {
        string oldSegment = string.Join(LF, oldLines);
        string newSegment = CreateNewSegment(oldSegment, oldLines, hunkLineRange, newString);

        string[] oldLineArray = SplitLines(oldSegment);
        string[] newLineArray = SplitLines(newSegment);
        List<string> lines = CreateHunkLines(oldLineArray, newLineArray);

        return new()
        {
            OldStart = hunkLineRange.OldStartLine + 1,
            OldLines = oldLineArray.Length,
            NewStart = hunkLineRange.OldStartLine + hunkLineRange.LineDeltaBefore + 1,
            NewLines = newLineArray.Length,
            Lines = lines,
        };
    }

    private static string CreateNewSegment(string oldSegment, List<string> oldLines, HunkLineRange hunkLineRange, NormalizedString newString)
    {
        StringBuilder builder = new(oldSegment.Length + (newString.Value.Length * hunkLineRange.ReplacementRanges.Count));
        int copyStartOffset = 0;
        foreach (ReplacementLineRange replacementRange in hunkLineRange.ReplacementRanges)
        {
            int replacementStartOffset = GetSegmentOffset(oldLines, hunkLineRange.OldStartLine, replacementRange.OldStartInclusive);
            int replacementEndOffset = GetSegmentOffset(oldLines, hunkLineRange.OldStartLine, replacementRange.OldEndExclusive);
            builder.Append(oldSegment.AsSpan(copyStartOffset, replacementStartOffset - copyStartOffset));
            builder.Append(newString.Value);
            copyStartOffset = replacementEndOffset;
        }

        builder.Append(oldSegment.AsSpan(copyStartOffset));
        return builder.ToString();
    }

    private static int GetSegmentOffset(List<string> oldLines, int hunkStartLine, TextPosition targetPosition)
    {
        int relativeLine = targetPosition.Line - hunkStartLine;
        if (relativeLine >= oldLines.Count)
        {
            return string.Join(LF, oldLines).Length;
        }

        int offset = targetPosition.Column;
        for (int i = 0; i < relativeLine; i++)
        {
            offset += oldLines[i].Length + 1;
        }

        return offset;
    }

    private static List<string> CreateHunkLines(string[] oldLines, string[] newLines)
    {
        int commonPrefixLineCount = CountCommonPrefixLines(oldLines, newLines);
        int commonSuffixLineCount = CountCommonSuffixLines(oldLines, newLines, commonPrefixLineCount);
        List<string> lines = new(oldLines.Length + newLines.Length);

        for (int i = 0; i < commonPrefixLineCount; i++)
        {
            lines.Add(HunkContext + oldLines[i]);
        }

        int oldChangeEndLine = oldLines.Length - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < oldChangeEndLine; i++)
        {
            lines.Add(HunkDeletion + oldLines[i]);
        }

        int newChangeEndLine = newLines.Length - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < newChangeEndLine; i++)
        {
            lines.Add(HunkAddition + newLines[i]);
        }

        int oldSuffixStartLine = oldLines.Length - commonSuffixLineCount;
        for (int i = 0; i < commonSuffixLineCount; i++)
        {
            lines.Add(HunkContext + oldLines[oldSuffixStartLine + i]);
        }

        return lines;
    }

    private static int CountCommonPrefixLines(string[] oldLines, string[] newLines)
    {
        int maxLineCount = Math.Min(oldLines.Length, newLines.Length);
        int commonLineCount = 0;
        while (commonLineCount < maxLineCount && string.Equals(oldLines[commonLineCount], newLines[commonLineCount], StringComparison.Ordinal))
        {
            commonLineCount++;
        }

        return commonLineCount;
    }

    private static int CountCommonSuffixLines(string[] oldLines, string[] newLines, int commonPrefixLineCount)
    {
        int maxLineCount = Math.Min(oldLines.Length, newLines.Length) - commonPrefixLineCount;
        int commonLineCount = 0;
        while (commonLineCount < maxLineCount && string.Equals(oldLines[oldLines.Length - commonLineCount - 1], newLines[newLines.Length - commonLineCount - 1], StringComparison.Ordinal))
        {
            commonLineCount++;
        }

        return commonLineCount;
    }

    private static string[] SplitLines(string value)
    {
        string[] lines = value.Split(LF);
        if (lines.Length > 0 && lines[^1].Length is 0)
        {
            Array.Resize(ref lines, lines.Length - 1);
        }

        return lines;
    }

    private static void CountPatchLines(IReadOnlyList<AgentWorkspaceEditToolHunk> structuredPatch, out int additions, out int deletions)
    {
        additions = 0;
        deletions = 0;

        foreach (AgentWorkspaceEditToolHunk hunk in structuredPatch)
        {
            foreach (string line in hunk.Lines)
            {
                switch (line[0])
                {
                    case HunkAddition:
                        additions++;
                        break;
                    case HunkDeletion:
                        deletions++;
                        break;
                }
            }
        }
    }

    private sealed class ReplacementLineRange
    {
        public required TextPosition OldStartInclusive { get; init; }

        public required TextPosition OldEndExclusive { get; init; }

        public required int LineDelta { get; init; }
    }

    private sealed class HunkLineRange
    {
        public required int OldStartLine { get; init; }

        public int OldEndLine { get; set; }

        public required int LineDeltaBefore { get; init; }

        public int LineDelta { get; set; }

        public required List<ReplacementLineRange> ReplacementRanges { get; init; }
    }
}
