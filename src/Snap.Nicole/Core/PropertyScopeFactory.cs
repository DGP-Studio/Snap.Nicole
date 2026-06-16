namespace Snap.Nicole.Core;

internal class PropertyScopeFactory<T, TProperty>(T obj, Func<T, TProperty?, TProperty?> setProperty)
    where T : class
    where TProperty : class
{
    private readonly Func<T, TProperty?, TProperty?> setProperty = setProperty;
    private readonly T obj = obj;

    public PropertyScope<T, TProperty> Create(TProperty? enterValue, TProperty? exitValue)
    {
        return new(obj, setProperty, enterValue, exitValue);
    }
}