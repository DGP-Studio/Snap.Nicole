using Microsoft.Extensions.AI;
using Snap.Nicole.Core.Text;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ReadTool;

internal sealed class AgentWorkspaceReadTool : AgentWorkspaceTool
{
    private const int DefaultReadLineLimit = 2000;

    private readonly AITool tool;

    public AgentWorkspaceReadTool(AgentWorkspaceContext context) : base(context)
    {
        tool = AIFunctionFactory.Create(InvokeAsync);
    }

    public override AITool Tool { get => tool; }

    [DisplayName(Prompt.ReadToolName)]
    [Description(Prompt.ReadToolDescription)]
    private async Task<AIContent> InvokeAsync(
        [Description("The absolute path to the file to read")] string filePath,
        [Description("The line number to start reading from. Only provide if the file is too large to read at once")][DefaultValue(0)] int offset,
        [Description("The number of lines to read. Only provide if the file is too large to read at once.")][DefaultValue(null)] int? limit,
        CancellationToken cancellationToken = default)
    {
        switch (Context.ResolveWorkspaceFile(filePath))
        {
            case string message:
                return new TextContent(message);
            case AgentWorkspaceFile path:
                if (!File.Exists(path.FullPath))
                {
                    if (Directory.Exists(path.FullPath))
                    {
                        throw new InvalidOperationException($"'{path.FullPath}' is a directory. Use Glob to inspect directories.");
                    }

                    throw new FileNotFoundException($"File '{path.FullPath}' not found.", path.FullPath);
                }

                FileInfo fileInfo = new(path.FullPath);
                if (fileInfo.Length is 0)
                {
                    throw new InvalidOperationException($"File '{path.FullPath}' is empty.");
                }

                string extension = Path.GetExtension(path.FullPath);
                ulong readStartedHash = await path.ComputeHashAsync(cancellationToken);
                AIContent content = GetImageMediaType(extension) is { } imageMediaType
                    ? await ReadImageFileAsync(path, imageMediaType, cancellationToken)
                    : new TextContent(await ReadTextFileAsync(path.FullPath, offset, limit, cancellationToken));
                ulong readCompletedHash = await path.ComputeHashAsync(cancellationToken);
                if (readCompletedHash != readStartedHash)
                {
                    throw new InvalidOperationException($"File '{path.FullPath}' was modified while it was being read. Read it again before modifying.");
                }

                Context.MarkFileAsRead(path.FullPath, readCompletedHash);
                return content;
            default:
                throw new InvalidOperationException("Unexpected result from ResolveWorkspaceFile.");
        }
    }

    private static async Task<DataContent> ReadImageFileAsync(AgentWorkspaceFile file, string mediaType, CancellationToken cancellationToken)
    {
        using FileStream stream = file.OpenRead(share: FileShare.ReadWrite | FileShare.Delete);
        DataContent content = await DataContent.LoadFromAsync(stream, mediaType, cancellationToken);
        content.Name = Path.GetFileName(file.FullPath);
        return content;
    }

    private static async Task<string> ReadTextFileAsync(string fullPath, int offset, int? limit, CancellationToken cancellationToken)
    {
        int startLineNumber = NormalizeReadOffset(offset);
        int lineLimit = NormalizeReadLimit(limit);
        StringBuilder builder = new();
        int currentLineNumber = 0;
        int writtenLineCount = 0;
        using StreamReader reader = new(fullPath, Encoding.UTF8WithoutBOM, detectEncodingFromByteOrderMarks: true);
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

        return builder.ToString();
    }

    private static void AppendCatLine(StringBuilder builder, int lineNumber, string line)
    {
        builder.Append(lineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(6));
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
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            return "image/png";
        }

        if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "image/jpeg";
        }

        if (string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase))
        {
            return "image/gif";
        }

        if (string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase))
        {
            return "image/bmp";
        }

        if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            return "image/webp";
        }

        return null;
    }
}
