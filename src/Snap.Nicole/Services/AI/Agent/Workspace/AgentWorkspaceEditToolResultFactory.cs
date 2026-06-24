using Snap.Nicole.Core.Text;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal static class AgentWorkspaceEditToolResultFactory
{
    private const int HunkContextLineCount = 3;
    private const int StreamBufferSize = 8192;

    public static async Task<AgentWorkspaceEditToolResult> CreateAsync(AgentWorkspacePath path, string oldString, string newString, IReadOnlyList<long> matchStartIndexes, bool replaceAll, CancellationToken cancellationToken)
    {
        List<ReplacementLineRange> replacementRanges = await CreateReplacementLineRangesAsync(path.FullPath, oldString, newString, matchStartIndexes, cancellationToken);
        List<HunkLineRange> hunkLineRanges = CreateHunkLineRanges(replacementRanges);
        List<AgentWorkspaceEditToolHunk> structuredPatch = await CreateStructuredPatchAsync(path.FullPath, hunkLineRanges, oldString, newString, replaceAll, cancellationToken);
        string fileName = path.GetRootRelativePath(path.FullPath);
        CountPatchLines(structuredPatch, out int additions, out int deletions);

        return new()
        {
            FilePath = path.FullPath,
            OldString = oldString,
            NewString = newString,
            StructuredPatch = structuredPatch,
            UserModified = false,
            ReplaceAll = replaceAll,
            GitDiff = new()
            {
                FileName = fileName,
                Status = AgentWorkspaceEditToolGitDiffStatus.Modified,
                Additions = additions,
                Deletions = deletions,
                Changes = additions + deletions,
                Patch = CreateUnifiedPatch(fileName, structuredPatch),
            },
        };
    }

    private static async Task<List<ReplacementLineRange>> CreateReplacementLineRangesAsync(string sourcePath, string oldString, string newString, IReadOnlyList<long> matchStartIndexes, CancellationToken cancellationToken)
    {
        List<ReplacementLineRange> ranges = new(matchStartIndexes.Count);
        if (matchStartIndexes.Count is 0)
        {
            return ranges;
        }

        int replacementLineDelta = CountLineBreaks(newString) - CountLineBreaks(oldString);
        char[] buffer = new char[StreamBufferSize];
        int matchIndex = 0;
        int currentLineIndex = 0;
        long currentCharacterIndex = 0;
        bool pendingCarriageReturn = false;
        int currentMatchStartLine = 0;

        using FileStream stream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using StreamReader reader = new(stream, Encoding.UTF8WithoutBOM, detectEncodingFromByteOrderMarks: true);
        while (matchIndex < matchStartIndexes.Count)
        {
            int readCount = await reader.ReadAsync(buffer, cancellationToken);
            if (readCount is 0)
            {
                break;
            }

            for (int i = 0; i < readCount && matchIndex < matchStartIndexes.Count; i++)
            {
                char current = buffer[i];
                if (pendingCarriageReturn && current is not '\n')
                {
                    currentLineIndex++;
                    pendingCarriageReturn = false;
                }

                long matchStartIndex = matchStartIndexes[matchIndex];
                long matchEndIndex = matchStartIndex + oldString.Length - 1;

                if (currentCharacterIndex == matchStartIndex)
                {
                    currentMatchStartLine = currentLineIndex;
                }

                if (currentCharacterIndex == matchEndIndex)
                {
                    ranges.Add(new()
                    {
                        OldStartLine = currentMatchStartLine,
                        OldEndLine = currentLineIndex,
                        LineDelta = replacementLineDelta,
                    });
                    matchIndex++;
                }

                if (current is '\r')
                {
                    pendingCarriageReturn = true;
                }
                else if (current is '\n')
                {
                    currentLineIndex++;
                    pendingCarriageReturn = false;
                }

                currentCharacterIndex++;
            }
        }

        return ranges;
    }

    private static List<HunkLineRange> CreateHunkLineRanges(List<ReplacementLineRange> replacementRanges)
    {
        List<HunkLineRange> hunkRanges = [];
        int cumulativeLineDelta = 0;
        foreach (ReplacementLineRange replacementRange in replacementRanges)
        {
            int oldStartLine = Math.Max(0, replacementRange.OldStartLine - HunkContextLineCount);
            int oldEndLine = replacementRange.OldEndLine + HunkContextLineCount;

            if (hunkRanges.Count > 0 && oldStartLine <= hunkRanges[^1].OldEndLine + 1)
            {
                HunkLineRange currentRange = hunkRanges[^1];
                currentRange.OldEndLine = Math.Max(currentRange.OldEndLine, oldEndLine);
                currentRange.LineDelta += replacementRange.LineDelta;
            }
            else
            {
                hunkRanges.Add(new()
                {
                    OldStartLine = oldStartLine,
                    OldEndLine = oldEndLine,
                    LineDeltaBefore = cumulativeLineDelta,
                    LineDelta = replacementRange.LineDelta,
                });
            }

            cumulativeLineDelta += replacementRange.LineDelta;
        }

        return hunkRanges;
    }

    private static async Task<List<AgentWorkspaceEditToolHunk>> CreateStructuredPatchAsync(string sourcePath, List<HunkLineRange> hunkLineRanges, string oldString, string newString, bool replaceAll, CancellationToken cancellationToken)
    {
        List<AgentWorkspaceEditToolHunk> hunks = new(hunkLineRanges.Count);
        if (hunkLineRanges.Count is 0)
        {
            return hunks;
        }

        using FileStream stream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using StreamReader reader = new(stream, Encoding.UTF8WithoutBOM, detectEncodingFromByteOrderMarks: true);

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

            hunks.Add(CreateHunk(oldLines, hunkLineRange, oldString, newString, replaceAll));
        }

        return hunks;
    }

    private static AgentWorkspaceEditToolHunk CreateHunk(List<string> oldLines, HunkLineRange hunkLineRange, string oldString, string newString, bool replaceAll)
    {
        string oldSegment = string.Join('\n', oldLines);
        string newSegment = replaceAll
            ? oldSegment.Replace(oldString.ReplaceLineEndings("\n"), newString.ReplaceLineEndings("\n"), StringComparison.Ordinal)
            : ReplaceFirst(oldSegment, oldString.ReplaceLineEndings("\n"), newString.ReplaceLineEndings("\n"));

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

    private static string ReplaceFirst(string value, string oldString, string newString)
    {
        int matchIndex = value.IndexOf(oldString, StringComparison.Ordinal);
        if (matchIndex < 0)
        {
            throw new InvalidOperationException("String to replace not found in hunk.");
        }

        StringBuilder builder = new(value.Length - oldString.Length + newString.Length);
        builder.Append(value.AsSpan(0, matchIndex));
        builder.Append(newString);
        builder.Append(value.AsSpan(matchIndex + oldString.Length));
        return builder.ToString();
    }

    private static List<string> CreateHunkLines(string[] oldLines, string[] newLines)
    {
        int commonPrefixLineCount = CountCommonPrefixLines(oldLines, newLines);
        int commonSuffixLineCount = CountCommonSuffixLines(oldLines, newLines, commonPrefixLineCount);
        List<string> lines = new(oldLines.Length + newLines.Length);

        for (int i = 0; i < commonPrefixLineCount; i++)
        {
            lines.Add(" " + oldLines[i]);
        }

        int oldChangeEndLine = oldLines.Length - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < oldChangeEndLine; i++)
        {
            lines.Add("-" + oldLines[i]);
        }

        int newChangeEndLine = newLines.Length - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < newChangeEndLine; i++)
        {
            lines.Add("+" + newLines[i]);
        }

        int oldSuffixStartLine = oldLines.Length - commonSuffixLineCount;
        for (int i = 0; i < commonSuffixLineCount; i++)
        {
            lines.Add(" " + oldLines[oldSuffixStartLine + i]);
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
        string[] lines = value.Split('\n');
        if (lines.Length > 0 && lines[^1].Length is 0)
        {
            Array.Resize(ref lines, lines.Length - 1);
        }

        return lines;
    }

    private static int CountLineBreaks(string value)
    {
        int lineBreakCount = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] is '\r')
            {
                if (i + 1 < value.Length && value[i + 1] is '\n')
                {
                    i++;
                }

                lineBreakCount++;
            }
            else if (value[i] is '\n')
            {
                lineBreakCount++;
            }
        }

        return lineBreakCount;
    }

    private static void CountPatchLines(List<AgentWorkspaceEditToolHunk> structuredPatch, out int additions, out int deletions)
    {
        additions = 0;
        deletions = 0;
        foreach (AgentWorkspaceEditToolHunk hunk in structuredPatch)
        {
            foreach (string line in hunk.Lines)
            {
                if (line.StartsWith('+'))
                {
                    additions++;
                }
                else if (line.StartsWith('-'))
                {
                    deletions++;
                }
            }
        }
    }

    private static string CreateUnifiedPatch(string fileName, List<AgentWorkspaceEditToolHunk> structuredPatch)
    {
        StringBuilder builder = new();
        builder.AppendLine(CultureInfo.InvariantCulture, $"diff --git a/{fileName} b/{fileName}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"--- a/{fileName}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"+++ b/{fileName}");
        foreach (AgentWorkspaceEditToolHunk hunk in structuredPatch)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"@@ -{FormatRange(hunk.OldStart, hunk.OldLines)} +{FormatRange(hunk.NewStart, hunk.NewLines)} @@");
            foreach (string line in hunk.Lines)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    private static string FormatRange(int start, int lineCount)
    {
        if (lineCount is 1)
        {
            return start.ToString(CultureInfo.InvariantCulture);
        }

        return string.Create(CultureInfo.InvariantCulture, $"{start},{lineCount}");
    }

    private sealed class ReplacementLineRange
    {
        public required int OldStartLine { get; init; }

        public required int OldEndLine { get; init; }

        public required int LineDelta { get; init; }
    }

    private sealed class HunkLineRange
    {
        public required int OldStartLine { get; init; }

        public int OldEndLine { get; set; }

        public required int LineDeltaBefore { get; init; }

        public int LineDelta { get; set; }
    }
}
