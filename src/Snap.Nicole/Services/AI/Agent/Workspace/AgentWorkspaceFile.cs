using Snap.Nicole.Core.IO;
using System.Buffers;
using System.IO;
using System.IO.Hashing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFile : AgentWorkspacePath
{
    public bool Exists { get => File.Exists(FullPath); }

    public AgentWorkspaceDirectory Directory { get => AgentWorkspaceDirectory.Create(RootDirectory, Path.GetDirectoryName(FullPath)!); }

    public static AgentWorkspaceFile Create(string rootDirectory, string fullPath)
    {
        return new()
        {
            RootDirectory = rootDirectory,
            FullPath = fullPath,
        };
    }

    public TemporaryFileStream CreateTemporary(FileShare share = FileShare.Read, int bufferSize = 4096, FileOptions options = FileOptions.Asynchronous)
    {
        return new(FullPath, FileMode.CreateNew, FileAccess.Write, share, bufferSize, options);
    }

    public FileStream OpenRead(FileShare share = FileShare.Read, int bufferSize = 4096, FileOptions options = FileOptions.Asynchronous | FileOptions.SequentialScan)
    {
        return new(FullPath, FileMode.Open, FileAccess.Read, share, bufferSize, options);
    }

    public async Task<bool> IsEmptyOrWhitespaceAsync(Encoding encoding, CancellationToken cancellationToken = default)
    {
        const int StreamBufferSize = 1024;

        using IMemoryOwner<char> bufferOwner = MemoryPool<char>.Shared.Rent(StreamBufferSize);
        Memory<char> buffer = bufferOwner.Memory[..StreamBufferSize];

        using FileStream sourceStream = OpenRead(bufferSize: StreamBufferSize);
        using StreamReader reader = new(sourceStream, encoding, true);

        while (true)
        {
            int readCount = await reader.ReadAsync(buffer, cancellationToken);
            if (readCount is 0)
            {
                break;
            }

            if (!buffer.Span[..readCount].IsWhiteSpace())
            {
                return false;
            }
        }

        return true;
    }

    public async Task<ulong> ComputeHashAsync(CancellationToken cancellationToken = default)
    {
        const int StreamBufferSize = 81920;

        XxHash64 hash = new();
        using (FileStream stream = OpenRead(bufferSize: StreamBufferSize))
        {
            await hash.AppendAsync(stream, cancellationToken);
            return hash.GetCurrentHashAsUInt64();
        }
    }
}
