using Snap.Nicole.Services.AI.Models;
using System.Threading;
using Microsoft.Agents.AI;
using System.Threading.Tasks;
using Sentry;

namespace Snap.Nicole.Services.AI;

internal interface IAgentService
{
    ValueTask<HarnessAgent> CreateAgentAsync(ExtendedAgentOptions options, CancellationToken cancellationToken = default);

    ValueTask<SpanStatus> RunStreamingAsync(AgentRunStreamingContext context, TaskScheduler taskScheduler, CancellationToken cancellationToken);
}
