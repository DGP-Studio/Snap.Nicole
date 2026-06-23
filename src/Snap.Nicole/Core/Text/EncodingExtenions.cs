using System.Text;

namespace Snap.Nicole.Core.Text;

internal static class EncodingExtenions
{
    private static readonly Encoding utf8WithoutBOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    extension(Encoding)
    {
        public static Encoding UTF8WithoutBOM { get => utf8WithoutBOM; }
    }
}
