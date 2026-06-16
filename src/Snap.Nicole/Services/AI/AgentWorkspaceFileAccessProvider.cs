using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentWorkspaceFileAccessProvider : AIContextProvider
{
    private const string SaveFileFunctionName = "FileAccess_SaveFile";
    private const string ReadFileFunctionName = "FileAccess_ReadFile";
    private const string DeleteFileFunctionName = "FileAccess_DeleteFile";
    private const string ListFilesFunctionName = "FileAccess_ListFiles";
    private const string SearchFilesFunctionName = "FileAccess_SearchFiles";

    private readonly AgentFileStore fileStore;
    private readonly string workingDirectory;
    private readonly IReadOnlyList<AITool> tools;

    public AgentWorkspaceFileAccessProvider(AgentFileStore fileStore, string workingDirectory)
    {
        this.fileStore = fileStore;
        this.workingDirectory = workingDirectory;
        tools =
        [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(SaveFileAsync, CreateFunctionOptions(SaveFileFunctionName, "Writes UTF-8 text to a file under the workspace root. This operation requires user approval."))),
            AIFunctionFactory.Create(ReadFileAsync, CreateFunctionOptions(ReadFileFunctionName, "Reads UTF-8 text from a file under the workspace root.")),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DeleteFileAsync, CreateFunctionOptions(DeleteFileFunctionName, "Deletes a file under the workspace root. This operation requires user approval."))),
            AIFunctionFactory.Create(ListFilesAsync, CreateFunctionOptions(ListFilesFunctionName, "Lists direct child files under a workspace directory.")),
            AIFunctionFactory.Create(SearchFilesAsync, CreateFunctionOptions(SearchFilesFunctionName, "Searches direct child files under a workspace directory with a regular expression.")),
        ];
    }

    public override IReadOnlyList<string> StateKeys { get => Array.Empty<string>(); }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        AIContext aiContext = new()
        {
            Instructions = CreateInstructions(),
            Tools = tools,
        };

        return new(aiContext);
    }

    private async Task<string> SaveFileAsync([Description("Workspace-relative file path. Absolute paths and '..' segments are rejected.")] string fileName, [Description("UTF-8 text content to write.")] string content, [Description("Set true to overwrite an existing file.")] bool overwrite = false, CancellationToken cancellationToken = default)
    {
        string normalizedPath = NormalizeFilePath(fileName);
        if (!overwrite && await fileStore.FileExistsAsync(normalizedPath, cancellationToken))
        {
            return $"File already exists: {normalizedPath}";
        }

        await fileStore.WriteFileAsync(normalizedPath, content, cancellationToken);
        return $"Saved file: {normalizedPath}";
    }

    private async Task<string> ReadFileAsync([Description("Workspace-relative file path. Absolute paths and '..' segments are rejected.")] string fileName, CancellationToken cancellationToken = default)
    {
        string normalizedPath = NormalizeFilePath(fileName);
        string? content = await fileStore.ReadFileAsync(normalizedPath, cancellationToken);
        if (content is null)
        {
            return $"File not found: {normalizedPath}";
        }

        return content;
    }

    private async Task<string> DeleteFileAsync([Description("Workspace-relative file path. Absolute paths and '..' segments are rejected.")] string fileName, CancellationToken cancellationToken = default)
    {
        string normalizedPath = NormalizeFilePath(fileName);
        bool deleted = await fileStore.DeleteFileAsync(normalizedPath, cancellationToken);
        return deleted ? $"Deleted file: {normalizedPath}" : $"File not found: {normalizedPath}";
    }

    private Task<IReadOnlyList<string>> ListFilesAsync([Description("Workspace-relative directory path. Use empty or null for the workspace root.")] string? directory = null, CancellationToken cancellationToken = default)
    {
        string normalizedDirectory = NormalizeDirectoryPath(directory);
        return fileStore.ListFilesAsync(normalizedDirectory, cancellationToken);
    }

    private Task<IReadOnlyList<FileSearchResult>> SearchFilesAsync([Description("Regular expression pattern matched against file contents.")] string regexPattern, [Description("Workspace-relative directory path. Use empty or null for the workspace root.")] string? directory = null, [Description("Optional glob pattern such as '*.cs' or '*.md'.")] string? filePattern = null, CancellationToken cancellationToken = default)
    {
        string normalizedDirectory = NormalizeDirectoryPath(directory);
        return fileStore.SearchFilesAsync(normalizedDirectory, regexPattern, filePattern, cancellationToken);
    }

    private string CreateInstructions()
    {
        return $"Workspace file access is rooted at: {workingDirectory}\nUse only relative paths for workspace file operations. Absolute paths and parent-directory traversal are rejected. Saving and deleting files require explicit user approval.";
    }

    private static AIFunctionFactoryOptions CreateFunctionOptions(string name, string description)
    {
        return new()
        {
            Name = name,
            Description = description,
        };
    }

    private static string NormalizeFilePath(string path)
    {
        string normalizedPath = NormalizeRelativePath(path);
        if (normalizedPath.Length is 0)
        {
            throw new ArgumentException("File path cannot be empty.", nameof(path));
        }

        return normalizedPath;
    }

    private static string NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return NormalizeRelativePath(path);
    }

    private static string NormalizeRelativePath(string path)
    {
        string normalizedPath = path.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalizedPath) || normalizedPath.StartsWith("/", StringComparison.Ordinal) || IsWindowsDriveRootedPath(normalizedPath))
        {
            throw new ArgumentException($"Path must be relative to the workspace root: {path}", nameof(path));
        }

        string[] segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            if (segment is "." or "..")
            {
                throw new ArgumentException($"Path cannot contain traversal segments: {path}", nameof(path));
            }
        }

        return string.Join("/", segments);
    }

    private static bool IsWindowsDriveRootedPath(string path)
    {
        return path.Length >= 2 && char.IsLetter(path[0]) && path[1] is ':';
    }
}
