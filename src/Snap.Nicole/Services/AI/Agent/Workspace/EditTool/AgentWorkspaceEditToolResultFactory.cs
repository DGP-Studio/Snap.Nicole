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
    private const int HunkContextLineCount = 3;
    private const int StreamBufferSize = 4096;

    public static async Task<AgentWorkspaceEditToolResult> CreateAsync(AgentWorkspaceFile file, NormalizedString oldString, NormalizedString newString, IReadOnlyList<StreamingTextMatch> matches, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReplacementTextSpan> replacementTextSpans = await CreateReplacementTextSpansAsync(file, oldString, matches, cancellationToken);
        IReadOnlyList<HunkDescription> hunkDescriptions = CreateHunkDescriptions(replacementTextSpans, newString.LineEndingCount - oldString.LineEndingCount);
        IReadOnlyList<AgentWorkspaceEditToolHunk> structuredPatch = await CreateStructuredPatchAsync(file, hunkDescriptions, newString, cancellationToken);
        CountPatchLines(structuredPatch, out int additions, out int deletions);

        return new()
        {
            FilePath = file.FullPath,
            Additions = additions,
            Deletions = deletions,
            StructuredPatch = structuredPatch,
        };
    }

    private static async Task<IReadOnlyList<ReplacementTextSpan>> CreateReplacementTextSpansAsync(AgentWorkspaceFile file, NormalizedString oldString, IReadOnlyList<StreamingTextMatch> matches, CancellationToken cancellationToken)
    {
        if (matches.Count is 0)
        {
            return [];
        }

        List<ReplacementTextSpan> spans = [with(matches.Count)];
        using LineEndingNormalizingTextReader reader = OpenNormalizedReader(file);
        using IMemoryOwner<char> bufferOwner = MemoryPool<char>.Shared.Rent(StreamBufferSize);
        Memory<char> buffer = bufferOwner.Memory[..StreamBufferSize];

        int currentMatchIndex = 0;
        long currentCharIndex = 0;
        TextPosition currentPosition = new(0, 0);

        while (currentMatchIndex < matches.Count)
        {
            int readCount = await reader.ReadAsync(buffer, cancellationToken);
            if (readCount is 0)
            {
                break;
            }

            for (int i = 0; i < readCount && currentMatchIndex < matches.Count; i++)
            {
                char current = buffer.Span[i];

                if (currentCharIndex == matches[currentMatchIndex].StartIndex)
                {
                    spans.Add(new()
                    {
                        Start = currentPosition,
                        End = currentPosition.Advance(oldString),
                    });

                    currentMatchIndex++;
                }

                currentPosition = currentPosition.Advance(current);
                currentCharIndex++;
            }
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

            // Merge with the previous hunk if they are adjacent or overlapping
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

    private static async Task<IReadOnlyList<AgentWorkspaceEditToolHunk>> CreateStructuredPatchAsync(AgentWorkspaceFile file, IReadOnlyList<HunkDescription> hunkDescriptions, NormalizedString newString, CancellationToken cancellationToken)
    {
        if (hunkDescriptions.Count is 0)
        {
            return [];
        }

        using (LineEndingNormalizingTextReader reader = OpenNormalizedReader(file))
        {
            List<AgentWorkspaceEditToolHunk> hunks = [with(hunkDescriptions.Count)];

            int currentLine = 0;
            foreach (HunkDescription hunkDescription in hunkDescriptions)
            {
                while (currentLine < hunkDescription.StartLine && await reader.ReadLineAsync(cancellationToken) is not null)
                {
                    currentLine++;
                }

                TextLineCollection oldLines = [];
                while (currentLine <= hunkDescription.EndLine && await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    oldLines.Add(line);
                    currentLine++;
                }

                hunks.Add(CreateHunk(hunkDescription, oldLines, newString));
            }

            return hunks;
        }
    }

    private static AgentWorkspaceEditToolHunk CreateHunk(HunkDescription hunkDescription, TextLineCollection oldLines, NormalizedString newString)
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

        return AgentWorkspaceEditToolHunk.Create(CreateHunkLines(oldLines.WithoutTrailingEmptyLine(), newLines, oldStartLine, newStartLine));
    }

    private static IReadOnlyList<AgentWorkspaceEditToolHunkLine> CreateHunkLines(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines, int oldStartLine, int newStartLine)
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

        List<AgentWorkspaceEditToolHunkLine> lines = [with(oldLines.Count + newLines.Count)];
        int oldLineNumber = oldStartLine;
        int newLineNumber = newStartLine;

        for (int i = 0; i < commonPrefixLineCount; i++)
        {
            lines.Add(AgentWorkspaceEditToolHunkLine.CreateContext(oldLineNumber, oldLines[i]));
            oldLineNumber++;
            newLineNumber++;
        }

        int oldChangeEndLine = oldLines.Count - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < oldChangeEndLine; i++)
        {
            lines.Add(AgentWorkspaceEditToolHunkLine.CreateDeletion(oldLineNumber, oldLines[i]));
            oldLineNumber++;
        }

        int newChangeEndLine = newLines.Count - commonSuffixLineCount;
        for (int i = commonPrefixLineCount; i < newChangeEndLine; i++)
        {
            lines.Add(AgentWorkspaceEditToolHunkLine.CreateAddition(newLineNumber, newLines[i]));
            newLineNumber++;
        }

        int oldSuffixStartLine = oldLines.Count - commonSuffixLineCount;
        for (int i = 0; i < commonSuffixLineCount; i++)
        {
            lines.Add(AgentWorkspaceEditToolHunkLine.CreateContext(oldLineNumber, oldLines[oldSuffixStartLine + i]));
            oldLineNumber++;
            newLineNumber++;
        }

        return lines;
    }

    private static LineEndingNormalizingTextReader OpenNormalizedReader(AgentWorkspaceFile file)
    {
        return new(new StreamReader(file.OpenRead(share: FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8WithoutBOM, true));
    }

    private static void CountPatchLines(IReadOnlyList<AgentWorkspaceEditToolHunk> structuredPatch, out int additions, out int deletions)
    {
        additions = 0;
        deletions = 0;

        foreach (AgentWorkspaceEditToolHunk hunk in structuredPatch)
        {
            foreach (AgentWorkspaceEditToolHunkLine line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case AgentWorkspaceEditToolHunkLineKind.Addition:
                        additions++;
                        break;
                    case AgentWorkspaceEditToolHunkLineKind.Deletion:
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
