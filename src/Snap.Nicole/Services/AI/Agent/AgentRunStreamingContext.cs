using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Agent.Models;
using Snap.Nicole.Services.AI.Observables;
using Snap.Nicole.ViewModels.Agent;

namespace Snap.Nicole.Services.AI.Agent;

internal sealed class AgentRunStreamingContext
{
    public required AgentConversationViewModel Conversation { get; init; }

    public required ChatMessage InputMessage { get; init; }

    public required HarnessAgent Agent { get; init; }

    public required AgentSession Session { get; init; }

    public required ExtendedAgentOptions Options { get; init; }

    public required AgentWorkspaceSnapshot Workspace { get; init; }

    public ObservableChatMessage? TargetResponseMessage { get; set; }

    // This method has to be called on the main thread
    public ObservableChatMessage EnsureTargetResponseMessage()
    {
        if (TargetResponseMessage is not { } responseMessage)
        {
            responseMessage = ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, Options.ModelId);
            Conversation.Messages.Add(responseMessage);
            TargetResponseMessage = responseMessage;
        }

        return responseMessage;
    }
}
