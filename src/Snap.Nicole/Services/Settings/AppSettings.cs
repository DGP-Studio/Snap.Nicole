using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Nicole.Core;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Snap.Nicole.Services.Settings;

[GeneratedCopyFrom<AppSettings>]
internal sealed partial class AppSettings : ObservableObject, ICopyFrom<AppSettings>, IOptionsObservableChildrenProvider
{
    public string Language
    {
        get;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && SetProperty(ref field, value))
            {
                StringResourceProxy.Default.CurrentCulture = CultureInfo.GetCultureInfo(value);
            }
        }
    } = StringResourceProxy.SupportedCultures[0];

    public bool IsMainNavigationPaneOpen { get; set => SetProperty(ref field, value); } = true;

    [JsonInclude]
    public AppAgentOptions AgentOptions { get; private set => SetProperty(ref field, value ?? new()); } = new();

    public ObservableSettingsCollection<ModelProviderProfile, Guid> ModelProviderProfiles { get; set => SetProperty(ref field, value ?? []); } = [];

    public Guid? SelectedModelProviderProfileId
    {
        get => ModelProviderProfiles.CurrentItemId;
        set => ModelProviderProfiles.CurrentItemId = value;
    }

    public IEnumerable<INotifyPropertyChanged> EnumerateObservableChildren()
    {
        yield return AgentOptions;

        foreach (INotifyPropertyChanged source in ModelProviderProfiles.EnumerateObservableChildren())
        {
            yield return source;
        }
    }
}
