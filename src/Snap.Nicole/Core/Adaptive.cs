namespace Snap.Nicole.Core;

internal static class Adaptive
{
    public static T Exchange<T>(ref T location1, T value)
    {
        T original = location1;
        location1 = value;
        return original;
    }
}