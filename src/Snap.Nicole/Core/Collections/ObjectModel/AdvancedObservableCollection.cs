using Snap.Nicole.Core.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Snap.Nicole.Core.Collections.ObjectModel;

internal sealed class AdvancedObservableCollection<T> : ObservableCollection<T>
    where T : class
{
    private T? currentItem;

    public T? CurrentItem
    {
        get => currentItem;
        set => SetCurrentItem(value);
    }

    public void MoveCurrentToFirst()
    {
        CurrentItem = Count is 0 ? null : this[0];
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (currentItem is not null && !Contains(currentItem))
        {
            SetCurrentItem(null);
        }

        base.OnCollectionChanged(e);
    }

    private void SetCurrentItem(T? value)
    {
        T? coercedValue = value is not null && Contains(value) ? value : null;
        if (ReferenceEquals(currentItem, coercedValue))
        {
            return;
        }

        currentItem = coercedValue;
        OnPropertyChanged(PropertyChangedEventArgs.CurrentItem);
    }
}
