using Microsoft.Extensions.DependencyInjection.Extensions;
using Snap.Nicole.Core;
using Snap.Nicole.Core.Text.Json;
using System.ComponentModel;
using System.Text.Json;

namespace Snap.Nicole.Services.Settings;

internal static class SettingsServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddJsonSettings<T>(string fileNameWithoutExtension)
            where T : class, INotifyPropertyChanged, ICopyFrom<T>, new()
        {
            services.TryAddSingleton<ISettingsProvider<T>>(sp => new JsonSettingsProvider<T>(fileNameWithoutExtension, sp.GetRequiredKeyedService<JsonSerializerOptions>(JsonSerializerOptionsKey.Settings)));

            return services;
        }
    }
}
