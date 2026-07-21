using Snap.Nicole.Core.Collections.Generic;
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
            return AgentWorkspaceStructuredPatch.Empty;
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

    private static IReadOnlyList<TextSpan> CreateReplacementTextSpans(Text sourceText, Text oldString, IReadOnlyList<int> matchStartPositions)
    {
        if (matchStartPositions.Count is 0)
        {
            return [];
        }

        ArrayBuilder<TextSpan> spans = new(matchStartPositions.Count);
        int previousMatchEnd = 0;
        foreach (int matchStart in matchStartPositions)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(matchStart, previousMatchEnd);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(matchStart, sourceText.Length - oldString.Length);

            TextSpan matchSpan = new(matchStart, oldString.Length);
            spans.Add(matchSpan);
            previousMatchEnd = matchSpan.End;
        }

        return spans.ToReadOnlyListAndClear();
    }

    private static IReadOnlyList<HunkDescription> CreateHunkDescriptions(TextLineCollection sourceLines, IReadOnlyList<TextSpan> replacementTextSpans, int replacementLineDelta)
    {
        ArrayBuilder<HunkDescription> descriptions = new();
        HunkDescriptionBuilder? current = null;
        int cumulativeLineDelta = 0;

        foreach (TextSpan replacementTextSpan in replacementTextSpans)
        {
            int oldStartLine = Math.Max(0, sourceLines.GetLineFromPosition(replacementTextSpan.Start).LineNumber - HunkContextLineCount);
            int oldEndPosition = replacementTextSpan.IsEmpty ? replacementTextSpan.Start : replacementTextSpan.End - 1;
            int oldEndLine = sourceLines.GetLineFromPosition(oldEndPosition).LineNumber + HunkContextLineCount;

            if (current is not null && oldStartLine <= current.EndLine + 1)
            {
                current.EndLine = Math.Max(current.EndLine, oldEndLine);
                current.ReplacementTextSpans.Add(replacementTextSpan);
            }
            else
            {
                if (current is not null)
                {
                    descriptions.Add(current.ToHunkDescription());
                }

                current = HunkDescriptionBuilder.Create(oldStartLine, oldEndLine, cumulativeLineDelta, replacementTextSpan);
            }

            cumulativeLineDelta += replacementLineDelta;
        }

        if (current is not null)
        {
            descriptions.Add(current.ToHunkDescription());
        }

        return descriptions.ToReadOnlyListAndClear();
    }

    private static IReadOnlyList<AgentWorkspaceStructuredPatchHunk> CreateStructuredPatch(TextLineCollection sourceLines, IReadOnlyList<HunkDescription> hunkDescriptions, Text newString)
    {
        if (hunkDescriptions.Count is 0)
        {
            return [];
        }

        ArrayBuilder<AgentWorkspaceStructuredPatchHunk> hunks = new(hunkDescriptions.Count);
        foreach (HunkDescription hunkDescription in hunkDescriptions)
        {
            int endLine = Math.Min(hunkDescription.EndLine, sourceLines.Count - 1);
            TextLineCollection oldLines = sourceLines.Slice(hunkDescription.StartLine, endLine - hunkDescription.StartLine + 1);
            hunks.Add(CreateHunk(hunkDescription, oldLines, newString));
        }

        return hunks.ToReadOnlyListAndClear();
    }

    private static AgentWorkspaceStructuredPatchHunk CreateHunk(HunkDescription hunkDescription, IReadOnlyList<TextLine> oldLines, Text newString)
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

    private static string GetText(IReadOnlyList<TextLine> lines)
    {
        TextLine firstLine = lines[0];
        int startIndex = firstLine.Start;
        int length = lines[^1].End - startIndex;

        // Substring returns original instance if startIndex is 0 and length is equal to the string length
        return firstLine.Text.Value.Substring(startIndex, length);
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

        ArrayBuilder<AgentWorkspaceStructuredPatchHunkLine> lines = new(oldLineCount + newLineCount);
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

        return lines.ToReadOnlyListAndClear();
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

        public required int EndLine { get; init; }

        public required int LineDeltaBefore { get; init; }

        public required IReadOnlyList<TextSpan> ReplacementTextSpans { get; init; }

        public static HunkDescription Create(int startLine, int endLine, int lineDeltaBefore, ArrayBuilder<TextSpan> replacementTextSpans)
        {
            return new()
            {
                StartLine = startLine,
                EndLine = endLine,
                LineDeltaBefore = lineDeltaBefore,
                ReplacementTextSpans = replacementTextSpans.ToReadOnlyListAndClear(),
            };
        }
    }

    private sealed class HunkDescriptionBuilder
    {
        public required int StartLine { get; init; }

        public required int EndLine { get; set; }

        public required int LineDeltaBefore { get; init; }

        public required ArrayBuilder<TextSpan> ReplacementTextSpans { get; init; }

        public static HunkDescriptionBuilder Create(int startLine, int endLine, int lineDeltaBefore, TextSpan replacementTextSpan)
        {
            ArrayBuilder<TextSpan> replacementTextSpans = new();
            replacementTextSpans.Add(replacementTextSpan);

            return new()
            {
                StartLine = startLine,
                EndLine = endLine,
                LineDeltaBefore = lineDeltaBefore,
                ReplacementTextSpans = replacementTextSpans,
            };
        }

        public HunkDescription ToHunkDescription()
        {
            return HunkDescription.Create(StartLine, EndLine, LineDeltaBefore, ReplacementTextSpans);
        }
    }
}
