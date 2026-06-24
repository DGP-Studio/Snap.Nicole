using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Core.IO;

internal static class FileExtensions
{
    extension(File)
    {
        public static void ClearReadOnlyAttribute(string path)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }

            if (attributes.HasFlag(FileAttributes.Directory) || !attributes.HasFlag(FileAttributes.ReadOnly))
            {
                return;
            }

            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        public static async Task<bool> IsEmptyOrWhitespaceAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
        {
            const int StreamBufferSize = 1024;

            bool isFileEmptyOrWhitespace = true;

            using IMemoryOwner<char> bufferOwner = MemoryPool<char>.Shared.Rent(StreamBufferSize);
            Memory<char> buffer = bufferOwner.Memory[..StreamBufferSize];
            using FileStream sourceStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

            using StreamReader reader = new(sourceStream, encoding, detectEncodingFromByteOrderMarks: true);

            while (true)
            {
                int readCount = await reader.ReadAsync(buffer, cancellationToken);
                if (readCount is 0)
                {
                    break;
                }

                if (!buffer.Span[..readCount].IsWhiteSpace())
                {
                    isFileEmptyOrWhitespace = false;
                    break;
                }
            }

            return isFileEmptyOrWhitespace;
        }
    }
}
