using Microsoft.Extensions.AI;
using Snap.Nicole.Core.Text;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

internal static class AgentWorkspaceReadToolResultFactory
{
    private const int DefaultReadLineLimit = 2000;

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

    // Common image extensions
    private static readonly FrozenSet<string> ImageExtensions =
    [
        with(StringComparer.OrdinalIgnoreCase),
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
    ];

    public static async Task<AIContent> CreateAsync(AgentWorkspaceFile file, int offset, int? limit, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(file.FullPath);
        return GetImageMediaType(extension) is { } imageMediaType
            ? await ReadImageFileAsync(file, imageMediaType, cancellationToken)
            : await ReadTextFileAsync(file, offset, limit, cancellationToken);
    }

    public static bool IsBinaryExtension(string extension)
    {
        return BinaryExtensions.Contains(extension);
    }

    public static bool IsSupportedImageExtension(string extension)
    {
        return ImageExtensions.Contains(extension);
    }

    private static async Task<DataContent> ReadImageFileAsync(AgentWorkspaceFile file, string mediaType, CancellationToken cancellationToken)
    {
        using FileStream stream = file.OpenRead(share: FileShare.ReadWrite | FileShare.Delete);
        DataContent content = await DataContent.LoadFromAsync(stream, mediaType, cancellationToken);
        content.Name = Path.GetFileName(file.FullPath);
        return content;
    }

    private static async Task<TextContent> ReadTextFileAsync(AgentWorkspaceFile file, int offset, int? limit, CancellationToken cancellationToken)
    {
        int normalizedOffset = Math.Max(1, offset);
        int normalizedLimit = Math.Max(1, limit ?? DefaultReadLineLimit);

        using (FileStream stream = file.OpenRead(FileShare.ReadWrite | FileShare.Delete))
        {
            using (StreamReader reader = new(stream, Encoding.UTF8WithoutBOM, detectEncodingFromByteOrderMarks: true))
            {
                StringBuilder builder = new();
                int currentLineNumber = 0;
                int writtenLineCount = 0;

                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    currentLineNumber++;
                    if (currentLineNumber < normalizedOffset)
                    {
                        continue;
                    }

                    if (writtenLineCount >= normalizedLimit)
                    {
                        break;
                    }

                    builder.Append($"{currentLineNumber}\t{line}");
                    writtenLineCount++;
                }

                if (currentLineNumber is 0)
                {
                    return new TextContent("<system-reminder>Warning: the file exists but the contents are empty.</system-reminder>");
                }

                if (writtenLineCount is 0)
                {
                    return new TextContent($"<system-reminder>Warning: the file exists but is shorter than the provided offset ({offset}).</system-reminder>");
                }

                return new TextContent(builder.ToString());
            }
        }
    }

    private static string? GetImageMediaType(string extension)
    {
        if (!ImageExtensions.Contains(extension))
        {
            return null;
        }

        return extension.ToUpperInvariant() switch
        {
            ".PNG" => "image/png",
            ".JPG" or ".JPEG" => "image/jpeg",
            ".GIF" => "image/gif",
            ".WEBP" => "image/webp",
            _ => throw new InvalidOperationException($"Unsupported image extension '{extension}'."),
        };
    }
}
