using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentRunStreamingContext
{
    public required HarnessAgent Agent { get; init; }

    public required ChatMessage Message { get; init; }

    public required ObservableChatMessageCollection Collection { get; init; }

    public required ExtendedAgentOptions Options { get; init; }

    public required AgentSession Session { get; init; }

    public Action<ObservableAIContent>? ConfigureContent { get; init; }

    public ObservableChatMessage? TargetResponseMessage { get; init; }
}
