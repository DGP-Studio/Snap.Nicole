using System.IO;
using System.Threading;

namespace Snap.Nicole.Core.IO;

internal sealed class TemporaryFileStream : FileStream
{
    private const int DefaultBufferSize = 4096;

    private readonly string sourceFilePath;
    private readonly string temporaryFilePath;

    private int committed;

    public TemporaryFileStream(string sourceFilePath, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
        : this(sourceFilePath, CreateTemporaryFilePath(sourceFilePath), mode, access, share, bufferSize, options)
    {
    }

    public TemporaryFileStream(string sourceFilePath, string temporaryFilePath, FileMode mode, FileAccess access, FileShare share)
        : this(sourceFilePath, temporaryFilePath, mode, access, share, DefaultBufferSize, FileOptions.None)
    {
    }

    public TemporaryFileStream(string sourceFilePath, string temporaryFilePath, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
        : base(Path.GetFullPath(temporaryFilePath), mode, access, share, bufferSize, options)
    {
        this.sourceFilePath = Path.GetFullPath(sourceFilePath);
        this.temporaryFilePath = Path.GetFullPath(temporaryFilePath);
    }

    public void Commit()
    {
        if (Interlocked.Exchange(ref committed, 1) is not 0)
        {
            return;
        }

        try
        {
            Dispose();
            File.Move(temporaryFilePath, sourceFilePath, true);
        }
        catch
        {
            TryDeleteTemporaryFile();
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            DeleteTemporaryFileIfNeeded();
        }
    }

    private void DeleteTemporaryFileIfNeeded()
    {
        if (Volatile.Read(ref committed) is not 0)
        {
            return;
        }

        TryDeleteTemporaryFile();
    }

    private void TryDeleteTemporaryFile()
    {
        try
        {
            File.Delete(temporaryFilePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateTemporaryFilePath(string sourceFilePath)
    {
        string fullPath = Path.GetFullPath(sourceFilePath);
        string? directory = Path.GetDirectoryName(fullPath);
        return Path.Combine(directory ?? string.Empty, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
    }
}
