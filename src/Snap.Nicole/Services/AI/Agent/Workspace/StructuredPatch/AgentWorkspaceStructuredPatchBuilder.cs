using Snap.Nicole.Core;
using Snap.Nicole.Core.Text;
using System.Collections.Generic;
using System.Text;

namespace Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

internal static class AgentWorkspaceStructuredPatchBuilder
{
    private const int HunkContextLineCount = 3;

    public static AgentWorkspaceStructuredPatch CreateOverwritePatch(string? originalText, string content)
    {
        Text sourceText = new(originalText ?? string.Empty);
        Text newString = new(content);
        if (string.Equals(sourceText.Value, newString.Value, StringComparison.Ordinal))
        {
            return Empty();
        }

        return CreateReplacementPatch(sourceText, sourceText, newString, [0]);
    }

    public static AgentWorkspaceStructuredPatch CreateReplacementPatch(Text sourceText, Text oldString, Text newString, IReadOnlyList<int> matchStartIndexes)
    {
        IReadOnlyList<TextSpan> replacementTextSpans = CreateReplacementTextSpans(sourceText, oldString, matchStartIndexes);
        TextLineCollection sourceLines = TextLineCollection.FromText(sourceText);
        IReadOnlyList<HunkDescription> hunkDescriptions = CreateHunkDescriptions(sourceLines, replacementTextSpans, newString.LineEndingCount - oldString.LineEndingCount);
        IReadOnlyList<AgentWorkspaceStructuredPatchHunk> hunks = CreateStructuredPatch(sourceLines, hunkDescriptions, newString);
        CountPatchLines(hunks, out int additions, out int deletions);

        return new()
        {
            Additions = additions,
            Deletions = deletions,
            Hunks = hunks,
        };
    }

    private static AgentWorkspaceStructuredPatch Empty()
    {
        return new()
        {
            Additions = 0,
            Deletions = 0,
            Hunks = [],
        };
    }

    private static IReadOnlyList<TextSpan> CreateReplacementTextSpans(Text sourceText, Text oldString, IReadOnlyList<int> matchStartIndexes)
    {
        if (matchStartIndexes.Count is 0)
        {
            return [];
        }

        List<TextSpan> spans = [with(matchStartIndexes.Count)];
        int previousMatchEnd = 0;
        foreach (int matchStartIndex in matchStartIndexes)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(matchStartIndex, previousMatchEnd);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(matchStartIndex, sourceText.Length - oldString.Length);

            spans.Add(new(matchStartIndex, oldString.Length));
            previousMatchEnd = matchStartIndex + oldString.Length;
        }

        return spans;
    }

    private static IReadOnlyList<HunkDescription> CreateHunkDescriptions(TextLineCollection sourceLines, IReadOnlyList<TextSpan> replacementTextSpans, int replacementLineDelta)
    {
        List<HunkDescription> descriptions = [];
        int cumulativeLineDelta = 0;

        foreach (TextSpan replacementTextSpan in replacementTextSpans)
        {
            int oldStartLine = Math.Max(0, sourceLines.GetLineFromPosition(replacementTextSpan.Start).LineNumber - HunkContextLineCount);
            int oldEndPosition = replacementTextSpan.IsEmpty ? replacementTextSpan.Start : replacementTextSpan.End - 1;
            int oldEndLine = sourceLines.GetLineFromPosition(oldEndPosition).LineNumber + HunkContextLineCount;

            if (descriptions.Count > 0 && oldStartLine <= descriptions[^1].EndLine + 1)
            {
                HunkDescription current = descriptions[^1];
                current.EndLine = Math.Max(current.EndLine, oldEndLine);
                current.ReplacementTextSpans.Add(replacementTextSpan);
            }
            else
            {
                descriptions.Add(new()
                {
                    StartLine = oldStartLine,
                    EndLine = oldEndLine,
                    LineDeltaBefore = cumulativeLineDelta,
                    ReplacementTextSpans = [replacementTextSpan],
                });
            }

            cumulativeLineDelta += replacementLineDelta;
        }

        return descriptions;
    }

    private static IReadOnlyList<AgentWorkspaceStructuredPatchHunk> CreateStructuredPatch(TextLineCollection sourceLines, IReadOnlyList<HunkDescription> hunkDescriptions, Text newString)
    {
        if (hunkDescriptions.Count is 0)
        {
            return [];
        }

        List<AgentWorkspaceStructuredPatchHunk> hunks = [with(hunkDescriptions.Count)];
        foreach (HunkDescription hunkDescription in hunkDescriptions)
        {
            int endLine = Math.Min(hunkDescription.EndLine, sourceLines.Count - 1);
            TextLine[] oldLines = GetLines(sourceLines, hunkDescription.StartLine, endLine);
            hunks.Add(CreateHunk(hunkDescription, oldLines, newString));
        }

        return hunks;
    }

    private static AgentWorkspaceStructuredPatchHunk CreateHunk(HunkDescription hunkDescription, TextLine[] oldLines, Text newString)
    {
        string oldSegment = GetText(oldLines);
        int oldSegmentStart = oldLines[0].Start;
        StringBuilder builder = new(oldSegment.Length + (newString.Value.Length * hunkDescription.ReplacementTextSpans.Count));
        int copyStartOffset = 0;
        foreach (TextSpan replacementTextSpan in hunkDescription.ReplacementTextSpans)
        {
            int replacementStartOffset = replacementTextSpan.Start - oldSegmentStart;
            int replacementEndOffset = replacementTextSpan.End - oldSegmentStart;
            builder.Append(oldSegment.AsSpan(copyStartOffset, replacementStartOffset - copyStartOffset));
            builder.Append(newString.Value);
            copyStartOffset = replacementEndOffset;
        }

        builder.Append(oldSegment.AsSpan(copyStartOffset));
        TextLineCollection newLines = TextLineCollection.FromText(builder.ToString());

        int oldStartLine = hunkDescription.StartLine + 1;
        int newStartLine = hunkDescription.StartLine + hunkDescription.LineDeltaBefore + 1;

        return AgentWorkspaceStructuredPatchHunk.Create(CreateHunkLines(oldLines, newLines, oldStartLine, newStartLine));
    }

    private static string GetText(TextLine[] lines)
    {
        TextLine firstLine = lines[0];
        int startIndex = firstLine.Start;
        int length = lines[^1].End - startIndex;

        // Substring returns original instance if startIndex is 0 and length is equal to the string length
        return firstLine.Text.Value.Substring(startIndex, length);
    }

    private static TextLine[] GetLines(TextLineCollection lines, int startLine, int endLine)
    {
        TextLine[] result = new TextLine[endLine - startLine + 1];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = lines[startLine + i];
        }

        return result;
    }

    private static IReadOnlyList<AgentWorkspaceStructuredPatchHunkLine> CreateHunkLines(IReadOnlyList<TextLine> oldLines, IReadOnlyList<TextLine> newLines, int oldStartLine, int newStartLine)
    {
        int oldLineCount = GetLineCountWithoutTrailingEmptyLine(oldLines);
        int newLineCount = GetLineCountWithoutTrailingEmptyLine(newLines);
        int maxLineCount = Math.Min(oldLineCount, newLineCount);
        int commonPrefixLineCount = 0;
        while (commonPrefixLineCount < maxLineCount && string.Equals(oldLines[commonPrefixLineCount].ToString(), newLines[commonPrefixLineCount].ToString(), StringComparison.Ordinal))
        {
            commonPrefixLineCount++;
        }

        int maxSuffixLineCount = maxLineCount - commonPrefixLineCount;
        int commonSuffixLineCount = 0;
        while (commonSuffixLineCount < maxSuffixLineCount && string.Equals(oldLines[oldLineCount - commonSuffixLineCount - 1].ToString(), newLines[newLineCount - commonSuffixLineCount - 1].ToString(), StringComparison.Ordinal))
        {
            commonSuffixLineCount++;
        }

        List<AgentWorkspaceStructuredPatchHunkLine> lines = [with(oldLineCount + newLineCount)];
        int oldLineNumber = oldStartLine;
        int newLineNumber = newStartLine;

        for (int i = 0; i < commonPrefixLineCount; i++)
        {
            lines.Add(AgentWorkspaceStructuredPatchHunkLine.CreateContext(oldLineNumber, oldLines[i].ToString()));
            oldLineNumber++;
            newLineNumber++;
        }

        int oldChangeEndLine = oldLineCount - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < oldChangeEndLine; i++)
        {
            lines.Add(AgentWorkspaceStructuredPatchHunkLine.CreateDeletion(oldLineNumber, oldLines[i].ToString()));
            oldLineNumber++;
        }

        int newChangeEndLine = newLineCount - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < newChangeEndLine; i++)
        {
            lines.Add(AgentWorkspaceStructuredPatchHunkLine.CreateAddition(newLineNumber, newLines[i].ToString()));
            newLineNumber++;
        }

        int oldSuffixStartLine = oldLineCount - commonSuffixLineCount;
        for (int i = 0; i < commonSuffixLineCount; i++)
        {
            lines.Add(AgentWorkspaceStructuredPatchHunkLine.CreateContext(oldLineNumber, oldLines[oldSuffixStartLine + i].ToString()));
            oldLineNumber++;
            newLineNumber++;
        }

        return lines;
    }

    private static int GetLineCountWithoutTrailingEmptyLine(IReadOnlyList<TextLine> lines)
    {
        return lines[^1].Length is 0 ? lines.Count - 1 : lines.Count;
    }

    private static void CountPatchLines(IReadOnlyList<AgentWorkspaceStructuredPatchHunk> structuredPatch, out int additions, out int deletions)
    {
        additions = 0;
        deletions = 0;

        foreach (AgentWorkspaceStructuredPatchHunk hunk in structuredPatch)
        {
            foreach (AgentWorkspaceStructuredPatchHunkLine line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case AgentWorkspaceStructuredPatchHunkLineKind.Addition:
                        additions++;
                        break;
                    case AgentWorkspaceStructuredPatchHunkLineKind.Deletion:
                        deletions++;
                        break;
                }
            }
        }
    }

    private sealed class HunkDescription
    {
        public required int StartLine { get; init; }

        public required int EndLine { get; set; }

        public required int LineDeltaBefore { get; init; }

        public required List<TextSpan> ReplacementTextSpans { get; init; }
    }
}
