using Microsoft.UI.Windowing;

namespace Snap.Nicole.UI.Windowing;

internal static class AppWindowExtensions
{
    extension(AppWindow appWindow)
    {
        public bool SafeIsShownInSwitchers
        {
            get => appWindow.IsShownInSwitchers;
            set
            {
                try
                {
                    // Some users use a custom task bar and which doesn't implement ITaskbarList
                    // WinUI use ITaskbarList.AddTab & .DeleteTab to show/hide tab as of now (WASDK 1.7)
                    // At Microsoft.UI.Windowing.dll
                    appWindow.IsShownInSwitchers = value;
                }
                catch (NotImplementedException)
                {
                    // SetShownInSwitchers failed.
                }
            }
        }
    }
}
