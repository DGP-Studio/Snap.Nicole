using Microsoft.Agents.AI;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationRuntimeCoordinator(IAgentService agentService)
{
    private readonly IAgentService agentService = agentService;

    public async ValueTask<HarnessAgent> EnsureConversationAgentAsync(AgentConversationViewModel conversation, ExtendedAgentOptions requestOptions, CancellationToken cancellationToken)
    {
        if (conversation.Agent is not null && requestOptions.AgentEquals(conversation.AgentOptions))
        {
            return conversation.Agent;
        }

        ResetConversationRuntime(conversation);
        conversation.Agent = await agentService.CreateAgentAsync(requestOptions, cancellationToken);
        conversation.AgentOptions = requestOptions;
        return conversation.Agent;
    }

    public async ValueTask<AgentSession> EnsureConversationSessionAsync(AgentConversationViewModel conversation, HarnessAgent agent, CancellationToken cancellationToken)
    {
        if (conversation.Session is not null)
        {
            return conversation.Session;
        }

        if (conversation.SerializedSessionState is JsonElement serializedState)
        {
            conversation.Session = await agentService.DeserializeSessionAsync(agent, serializedState, cancellationToken);
        }
        else
        {
            conversation.Session = await agentService.CreateSessionAsync(agent, cancellationToken);
        }

        return conversation.Session;
    }

    public async ValueTask PersistConversationSessionAsync(AgentConversationViewModel conversation, HarnessAgent agent, AgentSession session, CancellationToken cancellationToken)
    {
        JsonElement serializedState = await agentService.SerializeSessionAsync(agent, session, cancellationToken);
        conversation.SerializedSessionState = serializedState.Clone();
    }

    public static void ResetConversationRuntime(AgentConversationViewModel conversation)
    {
        conversation.Agent = null;
        conversation.AgentOptions = null;
        conversation.Session = null;
    }
}
