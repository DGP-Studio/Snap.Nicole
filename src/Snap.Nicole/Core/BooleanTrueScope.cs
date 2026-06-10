namespace Snap.Nicole.Core;

internal ref struct BooleanTrueScope : IDisposable
{
    private ref bool storage;

    public BooleanTrueScope(ref bool storage)
    {
        this.storage = ref storage;
        storage = true;
    }

    public static BooleanTrueScope Create(ref bool value)
    {
        return new(ref value);
    }

    public void Dispose()
    {
        storage = false;
    }
}
