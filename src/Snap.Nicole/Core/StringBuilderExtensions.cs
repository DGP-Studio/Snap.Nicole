using System.Text;

namespace Snap.Nicole.Core;

internal static class StringBuilderExtensions
{
    extension(StringBuilder stringBuilder)
    {
        public string ToStringAndClear()
        {
            string result = stringBuilder.ToString();
            stringBuilder.Clear();
            return result;
        }
    }
}