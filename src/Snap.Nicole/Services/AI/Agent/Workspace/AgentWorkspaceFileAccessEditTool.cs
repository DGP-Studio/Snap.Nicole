using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFileAccessEditTool : AgentWorkspaceFileAccessToolComponent
{
    private readonly AITool tool;

    public AgentWorkspaceFileAccessEditTool(AgentWorkspaceFileAccessContext context) : base(context)
    {
        tool = AIFunctionFactory.Create(EditAsync).AsApprovalRequired();
    }

    public override AITool Tool { get => tool; }

    [DisplayName(AgentWorkspaceFileAccessToolNames.Edit)]
    [Description("""
        Performs exact string replacement in a file.

        - You must Read the file in this conversation before editing, or the call will fail.
        - `old_string` must match the file exactly, including indentation, and be unique — the edit fails otherwise. Strip the Read line prefix (line number + tab) before matching.
        - `replace_all: true` replaces every occurrence instead.
        """)]
    private async Task<string> EditAsync([Description("The absolute path to the file to modify")] string file_path, [Description("The text to replace it with (must be different from old_string)")] string new_string, [Description("The text to replace")] string old_string, [Description("Replace all occurrences of old_string (default false)")] bool replace_all = false, CancellationToken cancellationToken = default)
    {
        AgentWorkspaceFileAccessContext.WorkspacePath path = Context.ResolveAbsoluteFilePath(file_path);
        if (!File.Exists(path.FullPath))
        {
            return $"File '{path.DisplayPath}' not found.";
        }

        if (!Context.WasFileRead(path.FullPath))
        {
            throw new InvalidOperationException($"File '{path.DisplayPath}' must be read with {AgentWorkspaceFileAccessToolNames.Read} before editing.");
        }

        if (old_string.Length is 0)
        {
            throw new ArgumentException("old_string cannot be empty.", nameof(old_string));
        }

        if (string.Equals(old_string, new_string, StringComparison.Ordinal))
        {
            throw new ArgumentException("new_string must be different from old_string.", nameof(new_string));
        }

        string content = await File.ReadAllTextAsync(path.FullPath, Encoding.UTF8, cancellationToken);
        ReplacementMatch replacementMatch = FindReplacementMatch(content, old_string, replace_all, path.DisplayPath);
        string newContent = replace_all ? content.Replace(old_string, new_string, StringComparison.Ordinal) : content.Remove(replacementMatch.FirstIndex, old_string.Length).Insert(replacementMatch.FirstIndex, new_string);
        await File.WriteAllTextAsync(path.FullPath, newContent, Encoding.UTF8, cancellationToken);
        return $"File '{path.DisplayPath}' edited. Replaced {replacementMatch.Count} occurrence(s).";
    }

    private static ReplacementMatch FindReplacementMatch(string content, string oldString, bool replaceAll, string displayPath)
    {
        int firstIndex = content.IndexOf(oldString, StringComparison.Ordinal);
        if (firstIndex < 0)
        {
            throw new InvalidOperationException($"old_string was not found in '{displayPath}'.");
        }

        int count = 1;
        int searchIndex = firstIndex + oldString.Length;
        while (searchIndex < content.Length)
        {
            int index = content.IndexOf(oldString, searchIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            count++;
            if (!replaceAll)
            {
                throw new InvalidOperationException($"old_string is not unique in '{displayPath}'. Pass replace_all true to replace every occurrence.");
            }

            searchIndex = index + oldString.Length;
        }

        return new()
        {
            FirstIndex = firstIndex,
            Count = count,
        };
    }

    private sealed class ReplacementMatch
    {
        public required int FirstIndex { get; init; }

        public required int Count { get; init; }
    }
}
