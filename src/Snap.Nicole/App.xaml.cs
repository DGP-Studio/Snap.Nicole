using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.Hosting;
using Snap.Nicole.Core.Threading;
using Snap.Nicole.Native;
using Snap.Nicole.Resources;
using Snap.Nicole.Services.Settings;
using Snap.Nicole.UI.Shell;
using Snap.Nicole.UI.Xaml.Windows;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading.Tasks;

namespace Snap.Nicole;

public partial class App : Application
{
    public App()
    {
        Threading = new ApplicationThreading();
        UnhandledException += OnUnhandledException;

        XamlUtilities.PatchFontAndScriptServicesGetDefaultFontNameString("ms-appx:///Assets/MiSans-Regular.ttf#MiSans");

        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        InitializeComponent();
    }

    public static new App Current { get => (App)Application.Current; }

    public static bool IsHostInitialized { get; set; }

    [NotNull]
    public static IHost? Host
    {
        get
        {
            Debug.Assert(IsHostInitialized);
            return field!;
        }
        set;
    }

    internal IApplicationThreading Threading { get; }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        SentryDiagnostics.CaptureUnhandledException(e.Exception, !e.Handled);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!IsHostInitialized)
        {
            return;
        }

        _ = LaunchAsync();
    }

    private static async Task LaunchAsync()
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AppLaunch, "Launch application shell");

        try
        {
            await Host.StartAsync();

            IServiceProvider serviceProvider = Host.Services;

            // AppSettings must be initialized on the UI thread
            StringResourceProxy.Default.CurrentCulture = CultureInfo.GetCultureInfo(serviceProvider.GetRequiredService<ISettingsProvider<AppSettings>>().CurrentValue.Language);
            serviceProvider.GetRequiredService<INotifyIcon>().Create();
            serviceProvider.GetRequiredService<IWindowLifeTime<MainWindow>>().Show();
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.AppLaunch);
        }
    }
}
