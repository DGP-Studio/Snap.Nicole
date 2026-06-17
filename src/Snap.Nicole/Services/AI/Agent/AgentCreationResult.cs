using Microsoft.Agents.AI;

namespace Snap.Nicole.Services.AI.Agent;

internal sealed class AgentCreationResult
{
    public required HarnessAgent Agent { get; init; }

    public IAsyncDisposable? Resources { get; init; }
}
