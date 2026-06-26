namespace Snap.Nicole.Core;

internal static class StringExtensions
{
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
