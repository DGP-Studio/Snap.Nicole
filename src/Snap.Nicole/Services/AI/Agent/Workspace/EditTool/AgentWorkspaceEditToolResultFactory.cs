using Microsoft.Extensions.AI;
using Snap.Nicole.Core.Text;
using Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

internal static class AgentWorkspaceEditToolResultFactory
{
    public static async Task<AIContent> CreateAsync(string callId, AgentWorkspaceFile file, string oldString, string newString, bool replaceAll, CancellationToken cancellationToken)
    {
        string fileText;
        if (oldString.Length is 0 && !file.Exists)
        {
            file.Directory.CreateDirectory();
            fileText = string.Empty;
        }
        else
        {
            fileText = await file.ReadAllTextAsync(Encoding.UTF8WithoutBOM, cancellationToken);
        }

        NormalizedString normalizedFileText = new(fileText);
        NormalizedString normalizedOldString = new(oldString);
        NormalizedString normalizedNewString = new(newString);

        if (normalizedOldString.IsEmpty)
        {
            if (!normalizedFileText.Value.AsSpan().IsWhiteSpace())
            {
                return new TextContent("Cannot create new file - file already exists.");
            }

            // In case the file is not fully empty, we will use the file text as the old string to replace
            normalizedOldString = normalizedFileText;
        }

        IReadOnlyList<int> matchStartIndexes = FindMatchStartIndexes(normalizedFileText, normalizedOldString);

        if (matchStartIndexes.Count is 0)
        {
            string message = $"""
                String to replace not found in file.
                String: '{oldString}'
                """;
            return new TextContent(message);
        }

        if (matchStartIndexes.Count > 1 && !replaceAll)
        {
            string message = $"""
                Found multiple matches of the string to replace, but replaceAll is false.
                To replace all occurrences, set replaceAll to true.
                To replace only one occurrence, please provide more context to uniquely identify the instance.
                String: {oldString}
                """;
            return new TextContent(message);
        }

        string normalizedUpdatedFileText = normalizedOldString.IsEmpty
            ? normalizedNewString.Value
            : normalizedFileText.Replace(normalizedOldString, normalizedNewString);

        string updatedFileText = (LineEnding.GetDominantLineEnding(fileText) ?? LineEnding.GetDominantLineEnding(newString)) is { } lineEnding
            ? normalizedUpdatedFileText.ReplaceLineEndings(lineEnding)
            : normalizedUpdatedFileText;

        AgentWorkspaceStructuredPatch structuredPatch = AgentWorkspaceStructuredPatchBuilder.CreateReplacementPatch(normalizedFileText, normalizedOldString, normalizedNewString, matchStartIndexes);

        AgentWorkspaceEditToolResult result = new()
        {
            FilePath = file.FullPath,
            Additions = structuredPatch.Additions,
            Deletions = structuredPatch.Deletions,
            StructuredPatch = structuredPatch.Hunks,
        };

        await file.WriteAllTextAsync(updatedFileText, Encoding.UTF8WithoutBOM, cancellationToken);
        AgentWorkspaceEditToolResult.TryAdd(callId, result);

        return new TextContent(replaceAll
            ? $"The file {file.FullPath} has been updated. All occurrences were successfully replaced."
            : $"The file {file.FullPath} has been updated successfully.");
    }

    private static IReadOnlyList<int> FindMatchStartIndexes(NormalizedString fileText, NormalizedString oldString)
    {
        if (oldString.IsEmpty)
        {
            return fileText.IsEmpty ? [0] : [];
        }

        List<int> matchStartIndexes = [];

        int searchStartIndex = 0;
        while (searchStartIndex < fileText.Length)
        {
            int matchStartIndex = fileText.IndexOf(oldString, searchStartIndex, StringComparison.Ordinal);
            if (matchStartIndex < 0)
            {
                break;
            }

            matchStartIndexes.Add(matchStartIndex);
            searchStartIndex = matchStartIndex + oldString.Length;
        }

        return matchStartIndexes;
    }
}
