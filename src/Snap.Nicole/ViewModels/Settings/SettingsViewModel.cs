using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.Settings;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Snap.Nicole.ViewModels.Settings;

internal sealed partial class SettingsViewModel(IServiceProvider serviceProvider) : ObservableObject
{
    public AppSettings Settings { get; } = serviceProvider.GetRequiredService<ISettingsProvider<AppSettings>>().CurrentValue;

    public SettingsGitSyncViewModel GitSync { get; } = serviceProvider.GetRequiredService<SettingsGitSyncViewModel>();

    public SettingsModelConfigurationViewModel ModelConfiguration { get; } = serviceProvider.GetRequiredService<SettingsModelConfigurationViewModel>();

    public IReadOnlyList<SettingsItem<string>> Languages { get; } = [.. StringResourceProxy.SupportedCultures.Select(name => new SettingsItem<string>(CultureInfo.GetCultureInfo(name).NativeName, name))];
}
