using Microsoft.Agents.AI;
using Sentry;
using Snap.Nicole.Services.AI.Agent.Models;
using Snap.Nicole.Services.AI.Agent.Workspace;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent;

internal interface IAgentService
{
    ValueTask<HarnessAgent> CreateAgentAsync(ExtendedAgentOptions options, AgentWorkspaceSnapshot workspace, CancellationToken cancellationToken = default);

    ValueTask<SpanStatus> RunStreamingAsync(AgentRunStreamingContext context, TaskScheduler taskScheduler, CancellationToken cancellationToken);
}
