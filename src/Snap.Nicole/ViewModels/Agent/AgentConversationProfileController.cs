using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.Settings;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationProfileController(ISettingsProvider<AppSettings> settingsProvider)
{
    private readonly AppSettings settings = settingsProvider.CurrentValue;

    public static bool CanCreateRequestOptions([NotNullWhen(true)] ModelProviderProfile? providerProfile, [NotNullWhen(true)] ModelProfile? modelProfile)
    {
        if (providerProfile is null)
        {
            return false;
        }

        if (modelProfile is null || string.IsNullOrWhiteSpace(modelProfile.ModelId))
        {
            return false;
        }

        if (!providerProfile.ModelProfiles.Contains(modelProfile))
        {
            return false;
        }

        return true;
    }

    public ExtendedAgentOptions? CreateRequestOptions(ModelProviderProfile? providerProfile, ModelProfile? modelProfile)
    {
        if (!CanCreateRequestOptions(providerProfile, modelProfile))
        {
            return null;
        }

        return ExtendedAgentOptions.Create(providerProfile, modelProfile, settings.AgentOptions);
    }

    public ModelProviderProfile? ResolveModelProviderProfile(Guid? providerProfileId)
    {
        // Callers should be transparent about model provider profile resolution.
        // So not like ResolveModelProfile, we do not accept ModelProviderProfiles for now.
        ObservableSettingsCollection<ModelProviderProfile, Guid> collection = settings.ModelProviderProfiles;

        if (!providerProfileId.HasValue)
        {
            return collection.CurrentItem;
        }

        return collection.FirstOrDefault(profile => profile.Id == providerProfileId.Value) ?? collection.CurrentItem;
    }

    public ModelProfile? ResolveModelProfile(ModelProviderProfile? providerProfile, Guid? modelProfileId)
    {
        if (providerProfile?.ModelProfiles is not { } collection)
        {
            return null;
        }

        if (!modelProfileId.HasValue)
        {
            return collection.CurrentItem;
        }

        return collection.FirstOrDefault(profile => profile.Id == modelProfileId.Value) ?? collection.CurrentItem;
    }
}
