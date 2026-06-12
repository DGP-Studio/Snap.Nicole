using System.Collections.Generic;
using System.ComponentModel;

namespace Snap.Nicole.Services.Settings;

internal sealed class HierarchyObserver<T, TState>
    where T : class, INotifyPropertyChanged
{
    private readonly T root;
    private readonly List<INotifyPropertyChanged> observableChildren = [];

    private readonly TState state;
    private readonly Action<TState> onPropertyChanged;

    private bool skipPropertyChanged;

    public HierarchyObserver(T root, TState state, Action<TState> onPropertyChanged)
    {
        this.root = root;
        root.PropertyChanged += OnPropertyChanged;

        this.state = state;
        this.onPropertyChanged = onPropertyChanged;
    }

    public ref bool SkipPropertyChanged { get => ref skipPropertyChanged; }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (SkipPropertyChanged)
        {
            return;
        }

        UpdateObservableChildren();
        onPropertyChanged(state);
    }

    private void UpdateObservableChildren()
    {
        ClearObservableChildren();

        if (root is not IObservableChildrenProvider provider)
        {
            return;
        }

        foreach (INotifyPropertyChanged source in provider.EnumerateObservableChildren())
        {
            source.PropertyChanged += OnPropertyChanged;
            observableChildren.Add(source);
        }
    }

    private void ClearObservableChildren()
    {
        foreach (INotifyPropertyChanged source in observableChildren)
        {
            source.PropertyChanged -= OnPropertyChanged;
        }

        observableChildren.Clear();
    }
}