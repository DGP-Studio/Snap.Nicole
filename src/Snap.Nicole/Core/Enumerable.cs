using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Snap.Nicole.Core;

internal static class Enumerable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> Enumerate<T>(params IEnumerable<T> values)
    {
        return values;
    }
}
