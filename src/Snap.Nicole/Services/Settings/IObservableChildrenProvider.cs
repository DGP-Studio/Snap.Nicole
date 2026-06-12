using System.Collections.Generic;
using System.ComponentModel;

namespace Snap.Nicole.Services.Settings;

internal interface IObservableChildrenProvider
{
    IEnumerable<INotifyPropertyChanged> EnumerateObservableChildren();
}
