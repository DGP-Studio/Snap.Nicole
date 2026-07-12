using System.Collections.Generic;

namespace Snap.Nicole.Core.Collections.Generic;

internal sealed class ArrayBuilder<T>
{
    private T[]? array;
    private int count;

    public void Add(T item)
    {
        if (array is null)
        {
            array = new T[4];
        }
        else if (count == array.Length)
        {
            Array.Resize(ref array, count * 2);
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

    public IReadOnlyList<T> ToReadOnlyListAndClear()
    {
        if (array is null)
        {
            return [];
        }

        T[] result = new T[count];
        Array.Copy(array, result, count);

        array = null;
        count = 0;
        return result;
    }
}
