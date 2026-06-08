using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Threading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sentry;
using System.Text.Json;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI;

internal interface IAgentService
{
    ValueTask<HarnessAgent> CreateAgentAsync(ExtendedAgentOptions options, CancellationToken cancellationToken = default);

    ValueTask<AgentSession> CreateSessionAsync(HarnessAgent agent, CancellationToken cancellationToken = default);

    ValueTask<AgentSession> DeserializeSessionAsync(HarnessAgent agent, JsonElement serializedState, CancellationToken cancellationToken = default);

    ValueTask<JsonElement> SerializeSessionAsync(HarnessAgent agent, AgentSession session, CancellationToken cancellationToken = default);

    ValueTask<SpanStatus> RunStreamingAsync(HarnessAgent agent, ChatMessage message, ObservableChatMessageCollection collection, ExtendedAgentOptions options, AgentSession session, TaskScheduler taskScheduler, CancellationToken cancellationToken = default);
}
