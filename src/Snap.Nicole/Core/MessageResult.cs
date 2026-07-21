using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Snap.Nicole.Core;

[Union]
internal readonly struct MessageResult<T> : IUnion
    where T : class
{
    private readonly string? message;
    private readonly T? result;
    private readonly long tag; // 0 = none, 1 = message, 2 = result

    public MessageResult(string message)
    {
        this.message = message;
        tag = 1;
    }

    public MessageResult(T result)
    {
        this.result = result;
        tag = 2;
    }

    private MessageResult(string? message, T? result, long tag)
    {
        this.message = message;
        this.result = result;
        this.tag = tag;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool HasValue { get => tag is not 0; }

    public object? Value
    {
        get => tag switch
        {
            1 => message,
            2 => result,
            _ => null,
        };
    }

    public static MessageResult<T> CastUp<TDerived>(MessageResult<TDerived> value)
        where TDerived : class, T
    {
        return new(value.message, value.result, value.tag);
    }

    public bool TryGetValue([NotNullWhen(true)] out string? value)
    {
        value = message;
        return tag is 1;
    }

    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = result;
        return tag is 2;
    }
}
