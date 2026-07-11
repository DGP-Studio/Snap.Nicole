using System.Runtime.CompilerServices;

namespace Snap.Nicole.Core;

internal static class NumberExtensions
{
    extension(int number)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEven()
        {
            return (number & 1) is 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsOdd()
        {
            return (number & 1) is 1;
        }
    }
}