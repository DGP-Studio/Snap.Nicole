using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.Windows.Globalization;
using Snap.Nicole.Core.ComponentModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace Snap.Nicole.Resources;

internal sealed class StringResourceProxy : ObservableObject
{
    public static IReadOnlyList<string> SupportedCultures { get; } = ["zh-Hans", "en"];

    public static StringResourceProxy Default { get; } = new();

    public CultureInfo CurrentCulture
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            CultureInfo.DefaultThreadCurrentCulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;

            CultureInfo.CurrentCulture = value;
            CultureInfo.CurrentUICulture = value;

            ApplicationLanguages.PrimaryLanguageOverride = value.Name;
            OnPropertyChanged(PropertyChangedEventArgs.Indexer);
        }
    } = CultureInfo.CurrentCulture;

    public string this[string name]
    {
        get => SR.GetString(string.Intern(name), CurrentCulture) ?? string.Empty;
    }

    public string this[SRName name]
    {
        get => this[$"{name}"];
    }

    public BindingBase CreateBinding(string name)
    {
        return new Binding
        {
            Source = this,
            Path = new PropertyPath(string.Intern($"[{name}]")),
            Mode = BindingMode.OneWay,
        };
    }
}
