using System.IO;

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
    }
}
