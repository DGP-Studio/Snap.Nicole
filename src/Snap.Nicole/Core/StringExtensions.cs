namespace Snap.Nicole.Core;

internal static class StringExtensions
{
    extension(string str)
    {
        public int CountLineEndings()
        {
            ReadOnlySpan<char> span = str;

            int count = 0;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] is '\r')
                {
                    if (i + 1 < span.Length && span[i + 1] is '\n')
                    {
                        i++;
                    }

                    count++;
                }
                else if (span[i] is '\n')
                {
                    count++;
                }
            }

            return count;
        }
    }

    extension(string? str)
    {
        public Uri? ToUri()
        {
            if (string.IsNullOrEmpty(str))
            {
                return null;
            }

            return new(str);
        }
    }
}
