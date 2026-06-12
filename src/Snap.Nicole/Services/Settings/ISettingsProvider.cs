using System.ComponentModel;

namespace Snap.Nicole.Services.Settings;

internal interface ISettingsProvider<out T>
    where T : class, INotifyPropertyChanged
{
    T CurrentValue { get; }
}
