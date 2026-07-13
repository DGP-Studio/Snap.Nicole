using System.Buffers;
using System.Collections.Generic;

namespace Snap.Nicole.Core.Collections.Generic;

internal sealed class ArrayBuilder<T>
{
    private T[]? array;
    private int count;

    public ArrayBuilder()
    {
    }

    public ArrayBuilder(int initialCapacity)
    {
        array = ArrayPool<T>.Shared.Rent(initialCapacity);
    }

    public void Add(T item)
    {
        if (array is null)
        {
            array = ArrayPool<T>.Shared.Rent(4);
        }
        else if (count == array.Length)
        {
            T[] newArray = ArrayPool<T>.Shared.Rent(count * 2);
            Array.Copy(array, newArray, count);
            ArrayPool<T>.Shared.Return(array);
        }

        array[count++] = item;
    }

    public void Sort(IComparer<T> comparer)
    {
        if (array is not null)
        {
            Array.Sort(array, 0, count, comparer);
        }
    }

    public T[] ToArrayAndClear()
    {
        if (array is null || count is 0)
        {
            return [];
        }

        T[] result = new T[count];
        Array.Copy(array, result, count);
        ArrayPool<T>.Shared.Return(array);

        array = null;
        count = 0;
        return result;
    }

    public IReadOnlyList<T> ToReadOnlyListAndClear()
    {
        return ToArrayAndClear();
    }
}
