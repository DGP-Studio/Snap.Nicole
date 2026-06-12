using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Nicole.Services.Settings;

namespace Snap.Nicole.ViewModels;

internal sealed partial class MainViewModel(IServiceProvider serviceProvider) : ObservableObject
{
    public AppSettings Settings { get; } = serviceProvider.GetRequiredService<ISettingsProvider<AppSettings>>().CurrentValue;
}
