using Snap.Nicole.Services.AI.Models;
using System.Threading;
using Microsoft.Agents.AI;
using System.Text.Json;
using System.Threading.Tasks;
using Sentry;

namespace Snap.Nicole.Services.AI;

internal interface IAgentService
{
    ValueTask<HarnessAgent> CreateAgentAsync(ExtendedAgentOptions options, CancellationToken cancellationToken = default);

    ValueTask<AgentSession> CreateSessionAsync(HarnessAgent agent, CancellationToken cancellationToken = default);

    ValueTask<AgentSession> DeserializeSessionAsync(HarnessAgent agent, JsonElement serializedState, CancellationToken cancellationToken = default);

    ValueTask<JsonElement> SerializeSessionAsync(HarnessAgent agent, AgentSession session, CancellationToken cancellationToken = default);

    ValueTask<SpanStatus> RunStreamingAsync(AgentRunStreamingContext context, TaskScheduler taskScheduler, CancellationToken cancellationToken);
}
