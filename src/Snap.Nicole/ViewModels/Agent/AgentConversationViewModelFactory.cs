using Snap.Nicole.Services.AI.Models;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationViewModelFactory(AgentConversationTurnController conversationTurnController)
{
    private readonly AgentConversationTurnController conversationTurnController = conversationTurnController;

    public AgentConversationViewModel Create(IAgentConversationDeleteHandler conversationDeleteHandler)
    {
        return new(conversationDeleteHandler, conversationTurnController);
    }

    public AgentConversationViewModel Create(AgentConversation data, IAgentConversationDeleteHandler conversationDeleteHandler)
    {
        AgentConversationViewModel conversation = Create(conversationDeleteHandler);
        conversation.Id = data.Id;
        conversation.Title = data.Title;
        conversation.CreatedAt = data.CreatedAt;
        conversation.UpdatedAt = data.UpdatedAt;
        conversation.Runtime.SerializedSessionState = data.SerializedSessionState?.Clone();
        conversation.Messages = new(data.Messages);
        conversation.ToolApprovalRequest = data.ToolApprovalRequest;
        conversation.UpdateConversationStatistics();
        return conversation;
    }
}
