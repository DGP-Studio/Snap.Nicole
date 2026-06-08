namespace Snap.Nicole.Core;

internal static class CopyFromExtensions
{
    public static bool TryCopyFrom<T>(this T target, T? source)
        where T : class, ICopyFrom<T>
    {
        if (source is null)
        {
            return false;
        }

        target.CopyFrom(source);
        return true;
    }
}