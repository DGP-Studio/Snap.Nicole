using Microsoft.Extensions.FileProviders;
using Sentry;
using Snap.Nicole.Core;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Snap.Nicole.Services.Settings;

// A single change in the options can trigger the whole object graph to be serialized and written to disk,
// so it's recommended to use this provider only for relatively small options objects and avoid putting large data in them.
internal sealed class JsonSettingsOptionsProvider<TOptions> : IOptionsProvider<TOptions>, IDisposable
    where TOptions : class, INotifyPropertyChanged, ICopyFrom<TOptions>, new()
{
    private const int LoadRetryCount = 3;
    private const int LoadRetryDelayMilliseconds = 50;

    private readonly Lock syncRoot = new();

    private readonly string filePath;
    private readonly string fileName;
    private readonly JsonSerializerOptions jsonOptions;

    private readonly PhysicalFileProvider fileProvider;
    private readonly List<INotifyPropertyChanged> observableChildren = [];
    private readonly TOptions cachedValue;

    private IDisposable? watchRegistration;
    private bool isExternalChange;
    private volatile bool disposed;

    public JsonSettingsOptionsProvider(string fileNameWithoutExtension, JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(jsonOptions);

        this.jsonOptions = jsonOptions;

        string directory = WellKnownLocations.Settings;
        Directory.CreateDirectory(directory);

        fileName = $"{fileNameWithoutExtension}.json";
        filePath = Path.Combine(directory, fileName);

        if (!TryLoadCore(useDefaultWhenMissing: true, out TOptions? value))
        {
            value = new TOptions();
        }

        cachedValue = value;
        cachedValue.PropertyChanged += OnRootPropertyChanged;
        UpdateObservableChildren();

        fileProvider = new(directory);
        StartWatchingFileChange();
    }

    public TOptions CurrentValue
    {
        get
        {
            lock (syncRoot)
            {
                return cachedValue;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, true))
        {
            return;
        }

        lock (syncRoot)
        {
            watchRegistration?.Dispose();
            fileProvider.Dispose();
        }

        cachedValue.PropertyChanged -= OnRootPropertyChanged;
        ClearObservableChildren();
    }

    private static void OnFileChanged(object? state)
    {
        if (state is not JsonSettingsOptionsProvider<TOptions> self)
        {
            return;
        }

        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsJsonFileChanged, $"Reload {self.fileName}");
        span.SetTag(SentryTags.SettingsOptions, typeof(TOptions).Name);

        if (self.disposed)
        {
            span.Finish(SpanStatus.Cancelled);
            return;
        }

        try
        {
            TOptions? newValue;
            bool loaded;

            lock (self.syncRoot)
            {
                loaded = self.TryLoadCore(useDefaultWhenMissing: false, out newValue);
            }

            if (loaded)
            {
                self.BeginApplyExternalChangeOnMainThread(newValue!);
            }
            else
            {
                span.Finish(SpanStatus.FailedPrecondition);
            }
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsJsonFileChanged);
        }
        finally
        {
            self.StartWatchingFileChange();
        }
    }

    private void StartWatchingFileChange()
    {
        if (disposed)
        {
            return;
        }

        IDisposable registration;
        try
        {
            registration = fileProvider.Watch(fileName).RegisterChangeCallback(OnFileChanged, this);
        }
        catch (ObjectDisposedException) when (disposed)
        {
            return;
        }

        lock (syncRoot)
        {
            if (disposed)
            {
                registration.Dispose();
                return;
            }

            watchRegistration?.Dispose();
            watchRegistration = registration;
        }
    }

    private void BeginApplyExternalChangeOnMainThread(TOptions value)
    {
        if (ReferenceEquals(SynchronizationContext.Current, App.Current.Threading.SynchronizationContext))
        {
            ApplyExternalChange(value);
            return;
        }

        App.Current.Threading.SynchronizationContext.Post(static state =>
        {
            if (state is (JsonSettingsOptionsProvider<TOptions> provider, TOptions change))
            {
                provider.ApplyExternalChange(change);
            }
        }, Tuple.Create(this, value));
    }

    private void ApplyExternalChange(TOptions value)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsJsonApplyExternalChange, $"Apply {fileName}");
        span.SetTag(SentryTags.SettingsOptions, typeof(TOptions).Name);

        if (disposed)
        {
            span.Finish(SpanStatus.Cancelled);
            return;
        }

        isExternalChange = true;
        try
        {
            cachedValue.CopyFrom(value);
            UpdateObservableChildren();
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsJsonApplyExternalChange);
        }
        finally
        {
            isExternalChange = false;
        }
    }

    private void OnRootPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isExternalChange)
        {
            return;
        }

        UpdateObservableChildren();
        PersistObservedChange();
    }

    private void OnObservableChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isExternalChange)
        {
            return;
        }

        UpdateObservableChildren();
        PersistObservedChange();
    }

    private void PersistObservedChange()
    {
        if (disposed || /* defensive check */ isExternalChange)
        {
            return;
        }

        lock (syncRoot)
        {
            if (disposed || /* defensive check */ isExternalChange)
            {
                return;
            }

            SaveCore(cachedValue);
        }
    }

    private void UpdateObservableChildren()
    {
        ClearObservableChildren();

        if (cachedValue is not IOptionsObservableChildrenProvider provider)
        {
            return;
        }

        foreach (INotifyPropertyChanged source in provider.EnumerateObservableChildren())
        {
            source.PropertyChanged += OnObservableChildPropertyChanged;
            observableChildren.Add(source);
        }
    }

    private void ClearObservableChildren()
    {
        foreach (INotifyPropertyChanged source in observableChildren)
        {
            source.PropertyChanged -= OnObservableChildPropertyChanged;
        }

        observableChildren.Clear();
    }

    private bool TryLoadCore(bool useDefaultWhenMissing, [NotNullWhen(true)] out TOptions? value)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsJsonLoad, $"Load {fileName}");
        span.SetTag(SentryTags.SettingsOptions, typeof(TOptions).Name);

        for (int retry = 0; retry < LoadRetryCount; retry++)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    span.SetTag(SentryTags.SettingsFileExists, false);
                    span.SetData(SentryData.SettingsLoadRetry, retry);
                    if (useDefaultWhenMissing)
                    {
                        value = new TOptions();
                        return true;
                    }

                    if (retry < LoadRetryCount - 1)
                    {
                        SentryDiagnostics.AddBreadcrumb("Retry missing settings load", SentryBreadcrumbCategories.SettingsJson, SentryBreadcrumbTypes.Default);
                        Thread.Sleep(LoadRetryDelayMilliseconds);
                        continue;
                    }

                    break;
                }

                span.SetTag(SentryTags.SettingsFileExists, true);
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    value = JsonSerializer.Deserialize<TOptions>(stream, jsonOptions) ?? new TOptions();
                    span.SetData(SentryData.SettingsLoadRetry, retry);
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                span.SetData(SentryData.SettingsLoadRetry, retry + 1);
                SentryDiagnostics.AddBreadcrumb("Retry settings load", SentryBreadcrumbCategories.SettingsJson, SentryBreadcrumbTypes.Default);
                if (retry == LoadRetryCount - 1)
                {
                    SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsJsonLoad);
                }

                Thread.Sleep(LoadRetryDelayMilliseconds);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or TargetInvocationException)
            {
                SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsJsonLoad);
                break;
            }
        }

        value = null;
        span.Finish(SpanStatus.FailedPrecondition);
        return false;
    }

    private void SaveCore(TOptions value)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsJsonSave, $"Save {fileName}");
        span.SetTag(SentryTags.SettingsOptions, typeof(TOptions).Name);

        string tempFile = $"{filePath}.tmp";

        try
        {
            using (FileStream stream = File.Open(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, jsonOptions);
            }

            FileHelper.ClearReadOnlyAttribute(filePath);
            File.Move(tempFile, filePath, true);
        }
        catch (Exception ex)
        {
            try
            {
                File.Delete(tempFile);
            }
            catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException)
            {
                // Preserve the original save failure as the observable error.
            }

            SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsJsonSave);
            throw;
        }
    }
}
