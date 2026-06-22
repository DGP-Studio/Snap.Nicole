using Microsoft.Extensions.ObjectPool;

namespace Snap.Nicole.Core.ObjectPool;

internal static class ObjectPoolExtensions
{
    extension<T>(ObjectPool<T> objectPool)
        where T : class
    {
        public ObjectPoolLease<T> Rent()
        {
            return new(objectPool);
        }
    }
}
