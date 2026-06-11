using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Microsoft.UI.Xaml.Controls;
using Sentry;
using Snap.Nicole.Core;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.Settings;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Settings;

internal sealed partial class SettingsModelConfigurationViewModel(IServiceProvider serviceProvider) : ObservableObject
{
    private const string RemovedModelNameSuffix = " [Removed]";

    private readonly IModelProfileService modelProfileService = serviceProvider.GetRequiredService<IModelProfileService>();

    public AppSettings Settings { get; } = serviceProvider.GetRequiredService<IOptionsProvider<AppSettings>>().CurrentValue;

    public IReadOnlyList<SettingsItem<EnumBox<ModelProviderType>>> ModelProviderTypes { get; } =
    [
        new("OpenAI Chat Completions | ~/chat/completions", EnumBox.Of(ModelProviderType.OpenAIChatCompletion)),
        new("OpenAI Responses | ~/responses", EnumBox.Of(ModelProviderType.OpenAIResponses)),
        new("Anthropic Messages | ~/v1/messages", EnumBox.Of(ModelProviderType.Anthropic)),
    ];

    // TODO: Use StringResourceValue for the display name.
    public IReadOnlyList<SettingsItem<EnumBox<ReasoningEffort>?>> ReasoningEfforts { get; } =
    [
        new("Provider default", null),
        new("None", EnumBox.Of(ReasoningEffort.None)),
        new("Low", EnumBox.Of(ReasoningEffort.Low)),
        new("Medium", EnumBox.Of(ReasoningEffort.Medium)),
        new("High", EnumBox.Of(ReasoningEffort.High)),
        new("Extra high", EnumBox.Of(ReasoningEffort.ExtraHigh)),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshModelsCommand))]
    public partial bool IsModelListBusy { get; set; }

    [ObservableProperty]
    public partial StringResourceValue ModelListStatusTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial StringResourceValue ModelListStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsModelListStatusOpen { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity ModelListInfoBarSeverity { get; set; } = InfoBarSeverity.Informational;

    private bool CanRefreshModels { get => !IsModelListBusy; }

    private static void MergeModelProfiles(ObservableSettingsCollection<ModelProfile, Guid> destination, IReadOnlyList<ModelProfile> source)
    {
        // There can potentially be models with the same ModelId, so we didn't use ToDictionary.
        Dictionary<string, ModelProfile> existingMap = new(StringComparer.Ordinal);
        foreach (ModelProfile destModelProfile in destination)
        {
            if (!string.IsNullOrWhiteSpace(destModelProfile.ModelId))
            {
                existingMap.TryAdd(destModelProfile.ModelId, destModelProfile);
            }
        }

        HashSet<string> visitedModelIds = new(StringComparer.Ordinal);
        bool shouldSelectFirstFetched = destination.CurrentItem is null || string.IsNullOrWhiteSpace(destination.CurrentItem.ModelId);
        ModelProfile? firstFetched = null;
        int targetIndex = 0;

        foreach (ModelProfile sourceModelProfile in source)
        {
            if (string.IsNullOrWhiteSpace(sourceModelProfile.ModelId) || !visitedModelIds.Add(sourceModelProfile.ModelId))
            {
                continue;
            }

            if (!existingMap.TryGetValue(sourceModelProfile.ModelId, out ModelProfile? current))
            {
                current = new();
                destination.Insert(targetIndex, current);
            }
            else
            {
                int currentIndex = destination.IndexOf(current);
                if (currentIndex != targetIndex)
                {
                    destination.Move(currentIndex, targetIndex);
                }
            }

            // Keep Id stable because conversations and selections reference model profiles by Guid.
            current.Name = sourceModelProfile.Name;
            current.ModelId = sourceModelProfile.ModelId;

            firstFetched ??= current;
            targetIndex++;
        }

        for (int i = targetIndex; i < destination.Count; i++)
        {
            ModelProfile modelProfile = destination[i];
            if (string.IsNullOrWhiteSpace(modelProfile.ModelId) || visitedModelIds.Contains(modelProfile.ModelId))
            {
                continue;
            }

            modelProfile.Name = GetRemovedModelProfileName(modelProfile);
        }

        if (shouldSelectFirstFetched && firstFetched is not null)
        {
            destination.CurrentItem = firstFetched;
        }
    }

    private static string GetRemovedModelProfileName(ModelProfile modelProfile)
    {
        string name = string.IsNullOrWhiteSpace(modelProfile.Name) ? modelProfile.ModelId : modelProfile.Name;
        if (name.EndsWith(RemovedModelNameSuffix, StringComparison.Ordinal))
        {
            return name;
        }

        return $"{name}{RemovedModelNameSuffix}";
    }

    [RelayCommand]
    private void AddProfile()
    {
        SentryDiagnostics.AddBreadcrumb("Add provider profile", SentryBreadcrumbCategories.SettingsModelProfiles, SentryBreadcrumbTypes.UI);

        ModelProviderProfile providerProfile = new();
        ModelProfile modelProfile = new();

        providerProfile.ModelProfiles.Add(modelProfile);
        providerProfile.ModelProfiles.CurrentItem = modelProfile;

        Settings.ModelProviderProfiles.Add(providerProfile);
        Settings.ModelProviderProfiles.CurrentItem = providerProfile;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (Settings.ModelProviderProfiles.CurrentItem is not { } selected)
        {
            return;
        }

        SentryDiagnostics.AddBreadcrumb("Delete provider profile", SentryBreadcrumbCategories.SettingsModelProfiles, SentryBreadcrumbTypes.UI);
        Settings.ModelProviderProfiles.Remove(selected);
    }

    [RelayCommand]
    private void AddModel()
    {
        if (Settings.ModelProviderProfiles.CurrentItem is not { } providerProfile)
        {
            return;
        }

        SentryDiagnostics.AddBreadcrumb("Add model profile", SentryBreadcrumbCategories.SettingsModelProfiles, SentryBreadcrumbTypes.UI);
        ModelProfile modelProfile = new();

        providerProfile.ModelProfiles.Add(modelProfile);
        providerProfile.ModelProfiles.CurrentItem = modelProfile;
    }

    [RelayCommand]
    private void DeleteModel()
    {
        if (Settings.ModelProviderProfiles.CurrentItem?.ModelProfiles is not { } modelProfiles)
        {
            return;
        }

        if (modelProfiles.CurrentItem is not { } selected)
        {
            return;
        }

        SentryDiagnostics.AddBreadcrumb("Delete model profile", SentryBreadcrumbCategories.SettingsModelProfiles, SentryBreadcrumbTypes.UI);
        modelProfiles.Remove(selected);
    }

    [RelayCommand]
    private void ClearModels()
    {
        SentryDiagnostics.AddBreadcrumb("Clear model profiles", SentryBreadcrumbCategories.SettingsModelProfiles, SentryBreadcrumbTypes.UI);
        Settings.ModelProviderProfiles.CurrentItem?.ModelProfiles?.Clear();
    }

    [RelayCommand(CanExecute = nameof(CanRefreshModels))]
    private async Task RefreshModelsAsync(CancellationToken cancellationToken)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsModelProfilesRefresh, "Refresh model list");

        if (Settings.ModelProviderProfiles.CurrentItem is not { } providerProfile)
        {
            SetModelListStatus(InfoBarSeverity.Warning, SRName.UIXamlPagesSettingsPageModelListStatusFailedTitle, SRName.UIXamlPagesSettingsPageModelListStatusNoProviderMessage);
            span.Finish(SpanStatus.FailedPrecondition);
            return;
        }

        span.SetTag(SentryTags.AIProvider, providerProfile.ProviderType.Value.ToString());

        if (!string.IsNullOrWhiteSpace(providerProfile.ModelListDocumentationLink))
        {
            OpenModelListDocumentationLink(providerProfile.ModelListDocumentationLink);
            return;
        }

        if (string.IsNullOrWhiteSpace(providerProfile.ApiKey))
        {
            SetModelListStatus(InfoBarSeverity.Warning, SRName.UIXamlPagesSettingsPageModelListStatusFailedTitle, SRName.UIXamlPagesSettingsPageModelListStatusApiKeyMissingMessage);
            span.Finish(SpanStatus.FailedPrecondition);
            return;
        }

        IsModelListBusy = true;
        SetModelListStatus(InfoBarSeverity.Informational, SRName.UIXamlPagesSettingsPageModelListStatusFetchingTitle, SRName.UIXamlPagesSettingsPageModelListStatusFetchingMessage);

        try
        {
            IReadOnlyList<ModelProfile> modelProfiles = await modelProfileService.GetModelsAsync(providerProfile, cancellationToken);
            MergeModelProfiles(providerProfile.ModelProfiles, modelProfiles);

            SetModelListStatus(InfoBarSeverity.Success, SRName.UIXamlPagesSettingsPageModelListStatusSuccessTitle, StringResourceValue.FromName(SRName.UIXamlPagesSettingsPageModelListStatusSuccessMessage, modelProfiles.Count));
            span.SetData(SentryData.AIModelCount, modelProfiles.Count);
        }
        catch (OperationCanceledException)
        {
            SetModelListStatus(InfoBarSeverity.Warning, SRName.UIXamlPagesSettingsPageModelListStatusFailedTitle, SRName.UIXamlPagesSettingsPageModelListStatusCanceledMessage);
            span.Finish(SpanStatus.Cancelled);
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsModelProfilesRefresh);
            SetModelListStatus(InfoBarSeverity.Error, SRName.UIXamlPagesSettingsPageModelListStatusFailedTitle, ex.Message);
        }
        finally
        {
            IsModelListBusy = false;
        }
    }

    private void OpenModelListDocumentationLink(string link)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsModelProfilesOpenDocs, "Open model list documentation");

        string trimmedLink = link.Trim();
        if (!Uri.TryCreate(trimmedLink, UriKind.Absolute, out Uri? uri) || uri.Scheme is not "http" and not "https")
        {
            SetModelListStatus(InfoBarSeverity.Error, SRName.UIXamlPagesSettingsPageModelListStatusFailedTitle, SRName.UIXamlPagesSettingsPageModelListStatusInvalidDocumentationLinkMessage);
            span.Finish(SpanStatus.InvalidArgument);
            return;
        }

        try
        {
            span.SetTag(SentryTags.UrlScheme, uri.Scheme);
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });

            SetModelListStatus(InfoBarSeverity.Informational, SRName.UIXamlPagesSettingsPageModelListStatusDocumentationOpenedTitle, SRName.UIXamlPagesSettingsPageModelListStatusDocumentationOpenedMessage);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsModelProfilesOpenDocs);
            SetModelListStatus(InfoBarSeverity.Error, SRName.UIXamlPagesSettingsPageModelListStatusFailedTitle, StringResourceValue.FromText(ex.Message));
        }
    }

    private void SetModelListStatus(InfoBarSeverity severity, StringResourceValue title, StringResourceValue message)
    {
        ModelListInfoBarSeverity = severity;
        ModelListStatusTitle = title;
        ModelListStatusMessage = message;
        IsModelListStatusOpen = true;
    }
}
