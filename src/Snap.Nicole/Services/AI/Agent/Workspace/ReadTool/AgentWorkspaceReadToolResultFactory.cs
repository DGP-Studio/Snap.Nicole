using Microsoft.Extensions.AI;
using Snap.Nicole.Core.Text;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

internal static class AgentWorkspaceReadToolResultFactory
{
    private const int DefaultReadLineLimit = 2000;

    public static async Task<AIContent> CreateAsync(AgentWorkspaceFile file, int offset, int? limit, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(file.FullPath);
        return GetImageMediaType(extension) is { } imageMediaType
            ? await ReadImageFileAsync(file, imageMediaType, cancellationToken)
            : await ReadTextFileAsync(file, offset, limit, cancellationToken);
    }

    internal static bool IsSupportedImageExtension(string extension)
    {
        return GetImageMediaType(extension) is not null;
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
        int startLineNumber = NormalizeReadOffset(offset);
        int lineLimit = NormalizeReadLimit(limit);
        StringBuilder builder = new();
        int currentLineNumber = 0;
        int writtenLineCount = 0;
        using FileStream stream = file.OpenRead(share: FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, Encoding.UTF8WithoutBOM, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            currentLineNumber++;
            if (currentLineNumber < startLineNumber)
            {
                continue;
            }

            if (writtenLineCount >= lineLimit)
            {
                AppendReadTruncationReminder(builder, currentLineNumber);
                break;
            }

            AppendCatLine(builder, currentLineNumber, line);
            writtenLineCount++;
        }

        if (currentLineNumber is 0)
        {
            throw new InvalidOperationException("File is empty.");
        }

        if (writtenLineCount is 0)
        {
            throw new InvalidOperationException($"Line offset {offset} is beyond the end of the file. The file has {currentLineNumber} line(s).");
        }

        return new TextContent(builder.ToString());
    }

    private static void AppendCatLine(StringBuilder builder, int lineNumber, string line)
    {
        builder.Append(lineNumber);
        builder.Append('\t');
        builder.AppendLine(line);
    }

    private static void AppendReadTruncationReminder(StringBuilder builder, int nextLineNumber)
    {
        builder.AppendLine($"[Output truncated. Use offset={nextLineNumber} to continue reading.]");
    }

    private static int NormalizeReadOffset(int offset)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "offset cannot be negative.");
        }

        return Math.Max(1, offset);
    }

    private static int NormalizeReadLimit(int? limit)
    {
        int normalizedLimit = limit ?? DefaultReadLineLimit;
        if (normalizedLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be greater than 0.");
        }

        return normalizedLimit;
    }

    private static string? GetImageMediaType(string extension)
    {
        return extension.ToUpperInvariant() switch
        {
            ".PNG" => "image/png",
            ".JPG" or ".JPEG" => "image/jpeg",
            ".GIF" => "image/gif",
            ".WEBP" => "image/webp",
            _ => null,
        };
    }
}
