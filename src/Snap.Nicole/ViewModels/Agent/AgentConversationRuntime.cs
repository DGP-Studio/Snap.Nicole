using Microsoft.Agents.AI;
using Snap.Nicole.Services.AI.Models;
using System.Text.Json;
using System.Threading.Tasks;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationRuntime
    : IDisposable, IAsyncDisposable
{
    private IAsyncDisposable? resources;

    public HarnessAgent? Agent { get; private set; }

    public ExtendedAgentOptions? AgentOptions { get; private set; }

    public AgentWorkspaceSnapshot? Workspace { get; private set; }

    public AgentSession? Session { get; set; }

    public JsonElement? SerializedSessionState { get; set; }

    public void Reset()
    {
        ResetAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask ResetAsync()
    {
        Agent = null;
        AgentOptions = null;
        Workspace = null;
        Session = null;
        await DisposeResourcesAsync();
    }

    public async ValueTask ResetAsync(HarnessAgent agent, ExtendedAgentOptions agentOptions, AgentWorkspaceSnapshot workspace, IAsyncDisposable? resources)
    {
        await DisposeResourcesAsync();
        Agent = agent;
        AgentOptions = agentOptions;
        Workspace = workspace;
        this.resources = resources;
        Session = null;
    }

    public void Dispose()
    {
        Reset();
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
    }

    private async ValueTask DisposeResourcesAsync()
    {
        IAsyncDisposable? oldResources = resources;
        resources = null;
        if (oldResources is not null)
        {
            await oldResources.DisposeAsync();
        }
    }
}
