using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;

namespace Snap.Nicole.Core.ObjectModel.Input;

internal static class RelayCommands
{
    public static void NotifyCanExecuteChanged(params IEnumerable<IRelayCommand> commands)
    {
        foreach (IRelayCommand command in commands)
        {
            command.NotifyCanExecuteChanged();
        }
    }
}
