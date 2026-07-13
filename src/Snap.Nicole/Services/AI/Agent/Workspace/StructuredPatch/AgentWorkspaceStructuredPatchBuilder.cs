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
        IReadOnlyList<ReplacementTextSpan> replacementTextSpans = CreateReplacementTextSpans(sourceText, oldString, matchStartIndexes);
        IReadOnlyList<HunkDescription> hunkDescriptions = CreateHunkDescriptions(replacementTextSpans, newString.LineEndingCount - oldString.LineEndingCount);
        IReadOnlyList<AgentWorkspaceStructuredPatchHunk> hunks = CreateStructuredPatch(sourceText, hunkDescriptions, newString);
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

    private static IReadOnlyList<ReplacementTextSpan> CreateReplacementTextSpans(Text sourceText, Text oldString, IReadOnlyList<int> matchStartIndexes)
    {
        if (matchStartIndexes.Count is 0)
        {
            return [];
        }

        List<ReplacementTextSpan> spans = [with(matchStartIndexes.Count)];
        int currentOffset = 0;
        TextPosition currentPosition = new(0, 0);
        foreach (int matchStartIndex in matchStartIndexes)
        {
            currentPosition = Advance(currentPosition, sourceText.Value.AsSpan(currentOffset, matchStartIndex - currentOffset));
            spans.Add(new()
            {
                Start = currentPosition,
                End = currentPosition.Advance(oldString),
            });

            currentPosition = currentPosition.Advance(oldString);
            currentOffset = matchStartIndex + oldString.Value.Length;
        }

        return spans;
    }

    private static IReadOnlyList<HunkDescription> CreateHunkDescriptions(IReadOnlyList<ReplacementTextSpan> replacementTextSpans, int replacementLineDelta)
    {
        List<HunkDescription> descriptions = [];
        int cumulativeLineDelta = 0;

        foreach (ReplacementTextSpan replacementTextSpan in replacementTextSpans)
        {
            int oldStartLine = Math.Max(0, replacementTextSpan.Start.Line - HunkContextLineCount);
            int oldEndLine = TextPosition.GetInclusiveEndLine(replacementTextSpan.Start, replacementTextSpan.End) + HunkContextLineCount;

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

    private static IReadOnlyList<AgentWorkspaceStructuredPatchHunk> CreateStructuredPatch(Text sourceText, IReadOnlyList<HunkDescription> hunkDescriptions, Text newString)
    {
        if (hunkDescriptions.Count is 0)
        {
            return [];
        }

        TextLineCollection sourceLines = TextLineCollection.FromText(sourceText.Value);
        List<AgentWorkspaceStructuredPatchHunk> hunks = [with(hunkDescriptions.Count)];

        foreach (HunkDescription hunkDescription in hunkDescriptions)
        {
            TextLineCollection oldLines = [];
            int endLine = Math.Min(hunkDescription.EndLine, sourceLines.Count - 1);

            for (int i = hunkDescription.StartLine; i <= endLine; i++)
            {
                oldLines = oldLines.Add(sourceLines[i]);
            }

            hunks.Add(CreateHunk(hunkDescription, oldLines, newString));
        }

        return hunks;
    }

    private static AgentWorkspaceStructuredPatchHunk CreateHunk(HunkDescription hunkDescription, TextLineCollection oldLines, Text newString)
    {
        string oldSegment = oldLines.ToText();
        StringBuilder builder = new(oldSegment.Length + (newString.Value.Length * hunkDescription.ReplacementTextSpans.Count));
        int copyStartOffset = 0;
        foreach (ReplacementTextSpan replacementTextSpan in hunkDescription.ReplacementTextSpans)
        {
            int replacementStartOffset = oldLines.GetTextOffset(hunkDescription.StartLine, replacementTextSpan.Start);
            int replacementEndOffset = oldLines.GetTextOffset(hunkDescription.StartLine, replacementTextSpan.End);
            builder.Append(oldSegment.AsSpan(copyStartOffset, replacementStartOffset - copyStartOffset));
            builder.Append(newString.Value);
            copyStartOffset = replacementEndOffset;
        }

        builder.Append(oldSegment.AsSpan(copyStartOffset));
        TextLineCollection newLines = TextLineCollection.FromText(builder.ToString());

        int oldStartLine = hunkDescription.StartLine + 1;
        int newStartLine = hunkDescription.StartLine + hunkDescription.LineDeltaBefore + 1;

        return AgentWorkspaceStructuredPatchHunk.Create(CreateHunkLines(oldLines.WithoutTrailingEmptyLine(), newLines, oldStartLine, newStartLine));
    }

    private static IReadOnlyList<AgentWorkspaceStructuredPatchHunkLine> CreateHunkLines(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines, int oldStartLine, int newStartLine)
    {
        int maxLineCount = Math.Min(oldLines.Count, newLines.Count);
        int commonPrefixLineCount = 0;
        while (commonPrefixLineCount < maxLineCount && string.Equals(oldLines[commonPrefixLineCount], newLines[commonPrefixLineCount], StringComparison.Ordinal))
        {
            commonPrefixLineCount++;
        }

        int maxSuffixLineCount = maxLineCount - commonPrefixLineCount;
        int commonSuffixLineCount = 0;
        while (commonSuffixLineCount < maxSuffixLineCount && string.Equals(oldLines[oldLines.Count - commonSuffixLineCount - 1], newLines[newLines.Count - commonSuffixLineCount - 1], StringComparison.Ordinal))
        {
            commonSuffixLineCount++;
        }

        List<AgentWorkspaceStructuredPatchHunkLine> lines = [with(oldLines.Count + newLines.Count)];
        int oldLineNumber = oldStartLine;
        int newLineNumber = newStartLine;

        for (int i = 0; i < commonPrefixLineCount; i++)
        {
            lines.Add(AgentWorkspaceStructuredPatchHunkLine.CreateContext(oldLineNumber, oldLines[i]));
            oldLineNumber++;
            newLineNumber++;
        }

        int oldChangeEndLine = oldLines.Count - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < oldChangeEndLine; i++)
        {
            lines.Add(AgentWorkspaceStructuredPatchHunkLine.CreateDeletion(oldLineNumber, oldLines[i]));
            oldLineNumber++;
        }

        int newChangeEndLine = newLines.Count - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < newChangeEndLine; i++)
        {
            lines.Add(AgentWorkspaceStructuredPatchHunkLine.CreateAddition(newLineNumber, newLines[i]));
            newLineNumber++;
        }

        int oldSuffixStartLine = oldLines.Count - commonSuffixLineCount;
        for (int i = 0; i < commonSuffixLineCount; i++)
        {
            lines.Add(AgentWorkspaceStructuredPatchHunkLine.CreateContext(oldLineNumber, oldLines[oldSuffixStartLine + i]));
            oldLineNumber++;
            newLineNumber++;
        }

        return lines;
    }

    private static TextPosition Advance(TextPosition position, ReadOnlySpan<char> value)
    {
        TextPosition result = position;
        for (int i = 0; i < value.Length; i++)
        {
            result = result.Advance(value[i]);
        }

        return result;
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

    private sealed class ReplacementTextSpan
    {
        public required TextPosition Start { get; init; }

        public required TextPosition End { get; init; }
    }

    private sealed class HunkDescription
    {
        public required int StartLine { get; init; }

        public required int EndLine { get; set; }

        public required int LineDeltaBefore { get; init; }

        public required List<ReplacementTextSpan> ReplacementTextSpans { get; init; }
    }
}
