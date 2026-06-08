using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentRunStreamingContext
{
    public required HarnessAgent Agent { get; init; }

    public required ChatMessage Message { get; init; }

    public required ObservableChatMessageCollection Collection { get; init; }

    public required ExtendedAgentOptions Options { get; init; }

    public required AgentSession Session { get; init; }

    public required TaskScheduler TaskScheduler { get; init; }

    public Action<ObservableAIContent>? ConfigureContent { get; init; }

    public CancellationToken CancellationToken { get; init; }

    public bool AddInputMessage { get; init; } = true;

    public ObservableChatMessage? TargetResponseMessage { get; init; }
}
