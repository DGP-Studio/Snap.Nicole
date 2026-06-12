using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Nicole.Core;
using Snap.Nicole.Services.Settings;
using System.Collections.Generic;
using System.ComponentModel;

namespace Snap.Nicole.Services.AI.Models;

[GeneratedCopyFrom<ModelProviderProfile>]
internal sealed partial class ModelProviderProfile : ObservableObject, IIdentifiable<Guid>, ICopyFrom<ModelProviderProfile>, IObservableChildrenProvider
{
    [ObservableProperty]
    public partial Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial EnumBox<ModelProviderType> ProviderType { get; set; } = EnumBox.Of(ModelProviderType.OpenAIChatCompletion);

    [ObservableProperty]
    public partial string Endpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ApiKey { get; set; }

    [ObservableProperty]
    public partial string? ModelListDocumentationLink { get; set; }

    public ObservableSettingsCollection<ModelProfile, Guid> ModelProfiles { get; set => SetProperty(ref field, value ?? []); } = [];

    public Guid? SelectedModelProfileId
    {
        get => ModelProfiles.CurrentItemId;
        set => ModelProfiles.CurrentItemId = value;
    }

    public IEnumerable<INotifyPropertyChanged> EnumerateObservableChildren()
    {
        return ModelProfiles.EnumerateObservableChildren();
    }
}
