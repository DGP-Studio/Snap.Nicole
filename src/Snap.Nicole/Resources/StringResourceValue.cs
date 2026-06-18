using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Nicole.Core.ComponentModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace Snap.Nicole.Resources;

internal sealed class StringResourceValue : ObservableObject
{
    private static readonly Lock registeredValuesLock = new();
    private static readonly List<WeakReference<StringResourceValue>> registeredValues = [];

    static StringResourceValue()
    {
        StringResourceProxy.Default.PropertyChanged += OnStringResourceChanged;
    }

    private StringResourceValue()
    {
        lock (registeredValuesLock)
        {
            registeredValues.Add(new(this));
        }
    }

    public SRName? Name { get; init; }

    public object?[]? Arguments { get; init; }

    public string Text { get; init; } = string.Empty;

    public string Value
    {
        get
        {
            if (Name is not SRName name)
            {
                return Text;
            }

            string value = StringResourceProxy.Default[name];
            if (Arguments is { Length: > 0 } arguments)
            {
                value = string.Format(StringResourceProxy.Default.CurrentCulture, value, Array.ConvertAll(arguments, NormalizeArgument));
            }

            return value;
        }
    }

    public static implicit operator StringResourceValue(SRName name)
    {
        return FromName(name);
    }

    public static implicit operator StringResourceValue(string? text)
    {
        return FromText(text);
    }

    public static StringResourceValue FromName(SRName name)
    {
        return new()
        {
            Name = name,
        };
    }

    public static StringResourceValue FromName(SRName name, params object?[]? arguments)
    {
        return new()
        {
            Name = name,
            Arguments = arguments,
        };
    }

    public static StringResourceValue FromText(string? text)
    {
        return new()
        {
            Text = text ?? string.Empty,
        };
    }

    private static void OnStringResourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!e.NameEquals(PropertyChangedEventArgs.Indexer))
        {
            return;
        }

        List<StringResourceValue> values = [];
        lock (registeredValuesLock)
        {
            for (int i = registeredValues.Count - 1; i >= 0; i--)
            {
                if (registeredValues[i].TryGetTarget(out StringResourceValue? value))
                {
                    values.Add(value);
                }
                else
                {
                    registeredValues.RemoveAt(i);
                }
            }
        }

        foreach (StringResourceValue value in values)
        {
            value.OnPropertyChanged(nameof(Value));
        }
    }

    private static object? NormalizeArgument(object? argument)
    {
        return argument switch
        {
            StringResourceValue value => value.Value,
            SRName name => StringResourceProxy.Default[name],
            _ => argument,
        };
    }
}
