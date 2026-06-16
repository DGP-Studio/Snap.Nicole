namespace Snap.Nicole.Core;

internal readonly ref struct PropertyScope<T, TProperty> : IDisposable
    where T : class
    where TProperty : class
{
    private readonly Func<T, TProperty?, TProperty?> setProperty;
    private readonly T obj;
    private readonly TProperty? exitValue;

    public PropertyScope(T obj, Func<T, TProperty?, TProperty?> setProperty, TProperty? enterValue, TProperty? exitValue)
    {
        this.setProperty = setProperty;
        this.obj = obj;
        this.exitValue = exitValue;

        setProperty(obj, enterValue);
    }

    public void Dispose()
    {
        setProperty(obj, exitValue);
    }
}
