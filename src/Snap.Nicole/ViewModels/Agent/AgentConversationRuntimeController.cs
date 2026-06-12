using Microsoft.Agents.AI;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationRuntimeController(IAgentService agentService)
{
    private readonly IAgentService agentService = agentService;

    public async ValueTask<HarnessAgent> EnsureConversationAgentAsync(AgentConversationRuntimeState runtime, ExtendedAgentOptions requestOptions, CancellationToken cancellationToken)
    {
        if (runtime.Agent is not null && requestOptions.AgentEquals(runtime.AgentOptions))
        {
            return runtime.Agent;
        }

        HarnessAgent agent = await agentService.CreateAgentAsync(requestOptions, cancellationToken);
        runtime.Reset(agent, requestOptions);
        return agent;
    }

    public async ValueTask<AgentSession> EnsureConversationSessionAsync(AgentConversationRuntimeState runtime, HarnessAgent agent, CancellationToken cancellationToken)
    {
        if (runtime.Session is not null)
        {
            return runtime.Session;
        }

        if (runtime.SerializedSessionState is JsonElement serializedState)
        {
            runtime.Session = await agent.DeserializeSessionAsync(serializedState, cancellationToken: cancellationToken);
        }
        else
        {
            runtime.Session = await agent.CreateSessionAsync(cancellationToken);
        }

        return runtime.Session;
    }

    public async ValueTask PersistConversationSessionAsync(AgentConversationRuntimeState runtime, HarnessAgent agent, AgentSession session, CancellationToken cancellationToken)
    {
        JsonElement serializedState = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        runtime.SerializedSessionState = serializedState.Clone();
    }
}
