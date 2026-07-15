using Microsoft.Agents.AI;
using Snap.Nicole.Core.Text.Json;
using Snap.Nicole.Services.AI.Agent;
using Snap.Nicole.Services.AI.Agent.Models;
using Snap.Nicole.Services.AI.Agent.Workspace;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationRuntimeController(IServiceProvider serviceProvider)
{
    private readonly IAgentService agentService = serviceProvider.GetRequiredService<IAgentService>();
    private readonly JsonSerializerOptions jsonOptions = serviceProvider.GetRequiredKeyedService<JsonSerializerOptions>(JsonSerializerOptionsKey.AgentConversation);

    public async ValueTask<HarnessAgent> EnsureConversationAgentAsync(AgentConversationViewModel conversation, ExtendedAgentOptions requestOptions, CancellationToken cancellationToken)
    {
        AgentConversationRuntime runtime = conversation.Runtime;
        AgentWorkspaceSnapshot workspace;
        try
        {
            workspace = AgentWorkspaceProvider.CreateSnapshot(conversation.Id, conversation.ExternalDirectories);
        }
        catch (Exception ex) when (AgentWorkspaceProvider.IsWorkspaceException(ex))
        {
            throw new AgentConversationException(ex.Message);
        }

        if (runtime.Agent is not null && requestOptions.AgentEquals(runtime.AgentOptions) && workspace.AgentEquals(runtime.Workspace))
        {
            return runtime.Agent;
        }

        HarnessAgent agent = await agentService.CreateAgentAsync(requestOptions, workspace, cancellationToken);
        runtime.Reset(agent, requestOptions, workspace);
        return agent;
    }

    public async ValueTask<AgentSession> EnsureConversationSessionAsync(AgentConversationRuntime runtime, HarnessAgent agent, CancellationToken cancellationToken)
    {
        if (runtime.Session is not null)
        {
            return runtime.Session;
        }

        if (runtime.SerializedSessionState is JsonElement serializedState)
        {
            runtime.Session = await agent.DeserializeSessionAsync(serializedState, jsonOptions, cancellationToken);
        }
        else
        {
            runtime.Session = await agent.CreateSessionAsync(cancellationToken);
        }

        return runtime.Session;
    }

    public async ValueTask SerializeConversationSessionAsync(AgentConversationRuntime runtime, HarnessAgent agent, AgentSession session, CancellationToken cancellationToken)
    {
        JsonElement serializedState = await agent.SerializeSessionAsync(session, jsonOptions, cancellationToken);
        runtime.SerializedSessionState = serializedState.Clone();
    }
}
