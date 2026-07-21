using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Core.Threading;

internal sealed class ApplicationThreading : IApplicationThreading
{
    public ApplicationThreading()
    {
        SynchronizationContext = SynchronizationContext.Current!;
        TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
    }

    public SynchronizationContext SynchronizationContext { get; }

    public TaskScheduler TaskScheduler { get; }
}
