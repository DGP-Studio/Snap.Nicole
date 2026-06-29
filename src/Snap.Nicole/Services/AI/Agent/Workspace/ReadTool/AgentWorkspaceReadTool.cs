using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

internal sealed class AgentWorkspaceReadTool(AgentWorkspaceContext context) : AgentWorkspaceTool(context)
{
    private const int ReadValidationErrorCode = 4;

    // Binary file extensions to skip for text-based operations.
    // These files can't be meaningfully compared as text and are often large.
    private static readonly FrozenSet<string> BinaryExtensions =
    [
        with(StringComparer.OrdinalIgnoreCase),

        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff", ".tif",

        // Videos
        ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".m4v", ".mpeg", ".mpg",

        // Audio
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma", ".aiff", ".opus",

        // Archives
        ".zip", ".tar", ".gz", ".bz2", ".7z", ".rar", ".xz", ".z", ".tgz", ".iso",

        // Executables/binaries
        ".exe", ".dll", ".so", ".dylib", ".bin", ".o", ".a", ".obj", ".lib", ".app", ".msi", ".deb", ".rpm",

        // Documents
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp",

        // Fonts
        ".ttf", ".otf", ".woff", ".woff2", ".eot",

        // Bytecode / VM artifacts
        ".pyc", ".pyo", ".class", ".jar", ".war", ".ear", ".node", ".wasm", ".rlib",

        // Database files
        ".sqlite", ".sqlite3", ".db", ".mdb", ".idx",

        // Design / 3D
        ".psd", ".ai", ".eps", ".sketch", ".fig", ".xd", ".blend", ".3ds", ".max",

        // Flash
        ".swf", ".fla",

        // Lock/profiling data
        ".lockb", ".dat", ".data",
    ];

    public override AITool Tool { get => field ??= AIFunctionFactory.Create(InvokeAsync); }

    [DisplayName(Prompt.ReadToolName)]
    [Description(Prompt.ReadToolDescription)]
    public Task<AIContent> InvokeAsync(
        [Description("The absolute path to the file to read")] string filePath,
        [Description("The line number to start reading from. Only provide if the file is too large to read at once")][DefaultValue(0)] int offset,
        [Description("The number of lines to read. Only provide if the file is too large to read at once.")][DefaultValue(null)] int? limit,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(filePath, offset, limit, cancellationToken);
    }

    private static ValidationResult Validate(AgentWorkspaceFile file)
    {
        if (!file.Exists)
        {
            if (Directory.Exists(file.FullPath))
            {
                return ValidationResult.Ask($"'{file.FullPath}' is a directory. Use Glob to inspect directories.", ReadValidationErrorCode);
            }

            return ValidationResult.Ask($"File '{file.FullPath}' not found.", ReadValidationErrorCode);
        }

        FileInfo fileInfo = new(file.FullPath);
        if (fileInfo.Length is 0)
        {
            return ValidationResult.Ask($"File '{file.FullPath}' is empty.", ReadValidationErrorCode);
        }

        string extension = Path.GetExtension(file.FullPath);
        if (HasBinaryExtension(extension) && !AgentWorkspaceReadToolResultFactory.IsSupportedImageExtension(extension))
        {
            string normalizedExtension = extension.ToLowerInvariant();
            return ValidationResult.Ask($"This tool cannot read binary files. The file appears to be a binary {normalizedExtension} file. Please use appropriate tools for binary file analysis.", ReadValidationErrorCode);
        }

        return ValidationResult.Ok;
    }

    private static bool HasBinaryExtension(string extension)
    {
        return BinaryExtensions.Contains(extension);
    }

    private async Task<AIContent> ReadAsync(string filePath, int offset, int? limit, CancellationToken cancellationToken = default)
    {
        MessageResult<AgentWorkspaceFile> result = Context.ResolveWorkspaceFile(filePath);
        if (result is string message)
        {
            return new TextContent(message);
        }

        if (result is AgentWorkspaceFile file)
        {
            ValidationResult validationResult = Validate(file);
            if (!validationResult.Result)
            {
                return new TextContent(validationResult.Message);
            }

            ulong readStartedHash = await file.ComputeHashAsync(cancellationToken);
            AIContent content = await AgentWorkspaceReadToolResultFactory.CreateAsync(file, offset, limit, cancellationToken);
            ulong readCompletedHash = await file.ComputeHashAsync(cancellationToken);
            if (readCompletedHash != readStartedHash)
            {
                throw new InvalidOperationException($"File '{file.FullPath}' was modified while it was being read. Read it again before modifying.");
            }

            Context.MarkFileAsRead(file.FullPath, readCompletedHash);
            return content;
        }

        throw new InvalidOperationException("Unexpected result from ResolveWorkspaceFile.");
    }
}
