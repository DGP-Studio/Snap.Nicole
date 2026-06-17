using Snap.Nicole.Services.AI.Agent.Models;
using System.Threading;
using Microsoft.Agents.AI;
using System.Threading.Tasks;
using Sentry;

namespace Snap.Nicole.Services.AI.Agent;

internal interface IAgentService
{
    ValueTask<AgentCreationResult> CreateAgentAsync(ExtendedAgentOptions options, AgentWorkspaceSnapshot workspace, CancellationToken cancellationToken = default);

    ValueTask<SpanStatus> RunStreamingAsync(AgentRunStreamingContext context, TaskScheduler taskScheduler, CancellationToken cancellationToken);
}
