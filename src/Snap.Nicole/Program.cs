global using Microsoft.Extensions.DependencyInjection;
global using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Snap.Nicole.Core.DependencyInjection;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.Text.Json;
using Snap.Nicole.Core.Threading;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Agent;
using Snap.Nicole.Services.AI.Agent.Workspace;
using Snap.Nicole.Services.Git;
using Snap.Nicole.Services.Settings;
using Snap.Nicole.UI.Shell;
using Snap.Nicole.UI.Xaml.Navigation;
using Snap.Nicole.UI.Xaml.Windows;
using Snap.Nicole.UI.Xaml.Workspace;
using Snap.Nicole.ViewModels;
using Snap.Nicole.ViewModels.Agent;
using Snap.Nicole.ViewModels.Agent.Workspace;
using Snap.Nicole.ViewModels.NotifyIcon;
using Snap.Nicole.ViewModels.Settings;
using System.Runtime.CompilerServices;
using System.Threading;
using WinRT;

[assembly: DisableRuntimeMarshalling]

namespace Snap.Nicole;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        AppContext.SetSwitch("System.Net.Http.EnableActivityPropagation", true);

        using IDisposable sentry = SentrySdkInitializationSupport.Initialize();
        using SentryDiagnosticSpan startupSpan = SentryDiagnostics.StartSpan(SentryOperations.AppStartup, "Initialize Snap.Nicole host");

        try
        {
            IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args);
            hostBuilder.ConfigureLogging(static logging =>
            {
                logging.AddSentry(SentrySdkInitializationSupport.ConfigureLogging);
            });

            hostBuilder.ConfigureServices(static (context, services) =>
            {
                services
                    .AddJsonSerializerOptions()
                    .AddJsonSettings<AppSettings>("AppSettings")
                    .AddXamlApplication<App>()
                    .AddXamlWindows(static builder =>
                    {
                        builder
                            .AddXamlWindow<MainWindow>()
                            .AddXamlWindow<NotifyIconXamlHostWindow>();
                    })
                    .AddSingleton<IMessenger>(WeakReferenceMessenger.Default)
                    .AddSingleton<INavigationService, NavigationService>()
                    .AddSingleton<INotifyIcon, NotifyIcon>()
                    .AddSingleton<AgentChatClientFactory>()
                    .AddSingleton<IAgentService, AgentService>()
                    .AddSingleton<IAgentWorkspaceInteractionService, AgentWorkspaceInteractionService>()
                    .AddSingleton<IAgentConversationProvider, AgentConversationFileProvider>()
                    .AddSingleton<IModelProfileService, ModelProfileService>()
                    .AddSingleton<ISettingsGitSyncService, SettingsGitSyncService>()
                    .AddTransient<NotifyIconContextMenuFlyoutViewModel>()
                    .AddTransient<MainViewModel>()
                    .AddTransient<HomeViewModel>()
                    .AddTransient<AgentViewModel>()
                    .AddTransient<AgentConversationPersistenceController>()
                    .AddTransient<AgentConversationProfileController>()
                    .AddTransient<AgentConversationRuntimeController>()
                    .AddTransient<AgentConversationTurnController>()
                    .AddTransient<AgentConversationWorkspaceController>()
                    .AddTransient<AgentConversationViewModelFactory>()
                    .AddTransient<AgentConversationCollectionController>()
                    .AddTransient<SettingsViewModel>()
                    .AddTransient<SettingsGitSyncViewModel>()
                    .AddTransient<SettingsModelConfigurationViewModel>();

                services
                    .AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>()
                    .AddSingleton(serviceProvider =>
                    {
                        ObjectPoolProvider poolProvider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                        return poolProvider.CreateStringBuilderPool(initialCapacity: 256, maximumRetainedCapacity: 4096);
                    });

            });

            App.Host = hostBuilder.Build();
            App.IsHostInitialized = true;
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, startupSpan, SentryOperations.AppStartup);
            throw;
        }

        Application.Start(static ignored =>
        {
            using SentryDiagnosticSpan xamlSpan = SentryDiagnostics.StartSpan(SentryOperations.AppXamlStart, "Start XAML application");

            SynchronizationContextPolyfill context = new(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);

            try
            {
                App app = App.Host.Services.GetRequiredService<App>();
            }
            catch (Exception ex)
            {
                SentryDiagnostics.CaptureException(ex, xamlSpan, SentryOperations.AppXamlStart);
                throw;
            }
        });
    }
}
