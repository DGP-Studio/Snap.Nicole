using Microsoft.Extensions.AI;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFileAccessReadTool : AgentWorkspaceFileAccessToolComponent
{
    private const int DefaultReadLineLimit = 2000;
    private const int MaxReadPdfPageCount = 20;

    private readonly AITool tool;

    public AgentWorkspaceFileAccessReadTool(AgentWorkspaceFileAccessContext context) : base(context)
    {
        tool = AIFunctionFactory.Create(ReadAsync);
    }

    public override AITool Tool { get => tool; }

    [DisplayName(AgentWorkspaceFileAccessToolNames.Read)]
    [Description("""
        Reads a file from the local filesystem.

        - `file_path` must be an absolute path.
        - Reads up to 2000 lines by default.
        - You can optionally specify a line offset and limit (especially handy for long files), but it's recommended to read the whole file by not providing these parameters
        - Results are returned using cat -n format, with line numbers starting at 1
        - Reads images (PNG, JPG, ...) and presents them visually. Reads PDFs via the `pages` parameter (e.g. "1-5", max 20 pages/request; required for PDFs over 10 pages). Reads Jupyter notebooks (.ipynb) as cells with outputs.
        - Reading a directory, a missing file, or an empty file returns an error or system reminder rather than content.
        - Do NOT re-read a file you just edited to verify - Edit/Write would have errored if the change failed, and the harness tracks file state for you.
        """)]
    private async Task<string> ReadAsync([Description("The absolute path to the file to read")] string file_path, [Description("The number of lines to read. Only provide if the file is too large to read at once.")][DefaultValue(null)] int? limit, [Description("The line number to start reading from. Only provide if the file is too large to read at once")][DefaultValue(0)] int offset, [Description("""Page range for PDF files (e.g., "1-5", "3", "10-20"). Only applicable to PDF files. Maximum 20 pages per request.""")][DefaultValue(null)] string? pages, CancellationToken cancellationToken = default)
    {
        AgentWorkspaceFileAccessContext.WorkspacePath path = Context.ResolveAbsoluteFilePath(file_path);
        if (!File.Exists(path.FullPath))
        {
            if (Directory.Exists(path.FullPath))
            {
                throw new InvalidOperationException($"'{path.DisplayPath}' is a directory. Use Glob to inspect directories.");
            }

            throw new FileNotFoundException($"File '{path.DisplayPath}' not found.", path.FullPath);
        }

        FileInfo fileInfo = new(path.FullPath);
        if (fileInfo.Length is 0)
        {
            throw new InvalidOperationException($"File '{path.DisplayPath}' is empty.");
        }

        string extension = Path.GetExtension(path.FullPath);
        if (!string.IsNullOrWhiteSpace(pages) && !IsPdfFile(extension))
        {
            throw new ArgumentException("pages is only applicable to PDF files.", nameof(pages));
        }

        if (IsPdfFile(extension))
        {
            ValidatePdfPages(pages);
            throw new NotSupportedException("PDF page rendering is not available in the current workspace file access provider.");
        }

        if (IsImageFile(extension))
        {
            throw new NotSupportedException("Image visual rendering is not available in the current workspace file access provider.");
        }

        string content = IsNotebookFile(extension) ? await ReadNotebookAsync(path.FullPath, offset, limit, cancellationToken) : await ReadTextFileAsync(path.FullPath, offset, limit, cancellationToken);
        Context.MarkFileAsRead(path.FullPath);
        return content;
    }

    private static async Task<string> ReadTextFileAsync(string fullPath, int offset, int? limit, CancellationToken cancellationToken)
    {
        int startLineNumber = NormalizeReadOffset(offset);
        int lineLimit = NormalizeReadLimit(limit);
        StringBuilder builder = new();
        int currentLineNumber = 0;
        int writtenLineCount = 0;
        using StreamReader reader = new(fullPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
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

    private static async Task<string> ReadNotebookAsync(string fullPath, int offset, int? limit, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(fullPath);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("cells", out JsonElement cellsElement) || cellsElement.ValueKind is not JsonValueKind.Array)
        {
            throw new InvalidOperationException("Notebook does not contain a cells array.");
        }

        StringBuilder notebook = new();
        int cellNumber = 0;
        foreach (JsonElement cellElement in cellsElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            cellNumber++;
            string cellType = TryGetJsonString(cellElement, "cell_type") ?? "unknown";
            notebook.AppendLine($"# Cell {cellNumber} ({cellType})");
            if (cellElement.TryGetProperty("source", out JsonElement sourceElement))
            {
                AppendNotebookText(notebook, sourceElement);
            }

            if (cellElement.TryGetProperty("outputs", out JsonElement outputsElement) && outputsElement.ValueKind is JsonValueKind.Array)
            {
                AppendNotebookOutputs(notebook, outputsElement);
            }

            notebook.AppendLine();
        }

        if (cellNumber is 0)
        {
            throw new InvalidOperationException("Notebook has no cells.");
        }

        return FormatNumberedLines(notebook.ToString(), offset, limit);
    }

    private static void AppendNotebookOutputs(StringBuilder builder, JsonElement outputsElement)
    {
        int outputNumber = 0;
        foreach (JsonElement outputElement in outputsElement.EnumerateArray())
        {
            outputNumber++;
            string outputType = TryGetJsonString(outputElement, "output_type") ?? "output";
            builder.AppendLine($"## Output {outputNumber} ({outputType})");
            if (outputElement.TryGetProperty("text", out JsonElement textElement))
            {
                AppendNotebookText(builder, textElement);
            }

            if (outputElement.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind is JsonValueKind.Object && dataElement.TryGetProperty("text/plain", out JsonElement plainTextElement))
            {
                AppendNotebookText(builder, plainTextElement);
            }

            if (outputElement.TryGetProperty("traceback", out JsonElement tracebackElement))
            {
                AppendNotebookText(builder, tracebackElement);
            }
        }
    }

    private static void AppendNotebookText(StringBuilder builder, JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.String)
        {
            builder.AppendLine(element.GetString());
            return;
        }

        if (element.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.String)
            {
                builder.Append(item.GetString());
            }
        }

        if (builder.Length > 0 && builder[^1] is not '\n')
        {
            builder.AppendLine();
        }
    }

    private static string FormatNumberedLines(string content, int offset, int? limit)
    {
        int startLineNumber = NormalizeReadOffset(offset);
        int lineLimit = NormalizeReadLimit(limit);
        string[] lines = content.ReplaceLineEndings("\n").Split('\n');
        int sourceLineCount = lines.Length > 0 && lines[^1].Length is 0 ? lines.Length - 1 : lines.Length;
        if (sourceLineCount is 0)
        {
            throw new InvalidOperationException("File is empty.");
        }

        StringBuilder builder = new();
        int writtenLineCount = 0;
        for (int i = startLineNumber; i <= sourceLineCount; i++)
        {
            if (writtenLineCount >= lineLimit)
            {
                AppendReadTruncationReminder(builder, i);
                break;
            }

            AppendCatLine(builder, i, lines[i - 1]);
            writtenLineCount++;
        }

        if (writtenLineCount is 0)
        {
            throw new InvalidOperationException($"Line offset {offset} is beyond the end of the file. The file has {sourceLineCount} line(s).");
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

    private static void ValidatePdfPages(string? pages)
    {
        if (string.IsNullOrWhiteSpace(pages))
        {
            return;
        }

        string[] parts = pages.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length is 0 or > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int startPage) || startPage <= 0)
        {
            throw new ArgumentException("Invalid PDF page range.", nameof(pages));
        }

        int endPage = startPage;
        if (parts.Length is 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out endPage) || endPage < startPage))
        {
            throw new ArgumentException("Invalid PDF page range.", nameof(pages));
        }

        if (endPage - startPage + 1 > MaxReadPdfPageCount)
        {
            throw new ArgumentException($"PDF page range cannot exceed {MaxReadPdfPageCount} pages.", nameof(pages));
        }
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement propertyElement) && propertyElement.ValueKind is JsonValueKind.String ? propertyElement.GetString() : null;
    }

    private static bool IsImageFile(string extension)
    {
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPdfFile(string extension)
    {
        return string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNotebookFile(string extension)
    {
        return string.Equals(extension, ".ipynb", StringComparison.OrdinalIgnoreCase);
    }
}
