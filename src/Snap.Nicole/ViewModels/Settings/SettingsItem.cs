using Snap.Nicole.Resources;

namespace Snap.Nicole.ViewModels.Settings;

internal sealed record SettingsItem<T>
{
    public SettingsItem(StringResourceValue label, T value)
    {
        Label = label;
        Value = value;
    }

    public StringResourceValue Label { get; }

    public T Value { get; }
}
