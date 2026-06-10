using Microsoft.Agents.AI;
using Snap.Nicole.Services.AI.Models;
using System.Text.Json;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationRuntimeState
{
    public HarnessAgent? Agent { get; private set; }

    public ExtendedAgentOptions? AgentOptions { get; private set; }

    public AgentSession? Session { get; set; }

    public JsonElement? SerializedSessionState { get; set; }

    public void Reset()
    {
        Agent = null;
        AgentOptions = null;
        Session = null;
    }

    public void Reset(HarnessAgent agent, ExtendedAgentOptions agentOptions)
    {
        Agent = agent;
        AgentOptions = agentOptions;
        Session = null;
    }
}
