using Microsoft.Agents.AI;
using Snap.Nicole.Services.AI.Agent.Models;
using Snap.Nicole.Services.AI.Agent.Workspace;
using System.Text.Json;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationRuntime
{
    public HarnessAgent? Agent { get; private set; }

    public ExtendedAgentOptions? AgentOptions { get; private set; }

    public AgentWorkspaceSnapshot? Workspace { get; private set; }

    public AgentSession? Session { get; set; }

    public JsonElement? SerializedSessionState { get; set; }

    public void Reset()
    {
        Agent = null;
        AgentOptions = null;
        Workspace = null;
        Session = null;
    }

    public void Reset(HarnessAgent agent, ExtendedAgentOptions agentOptions, AgentWorkspaceSnapshot workspace)
    {
        Agent = agent;
        AgentOptions = agentOptions;
        Workspace = workspace;
        Session = null;
    }
}
