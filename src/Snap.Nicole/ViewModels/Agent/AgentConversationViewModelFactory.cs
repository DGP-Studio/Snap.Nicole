using Snap.Nicole.Services.AI.Agent.Models;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationViewModelFactory(AgentConversationTurnController conversationTurnController, AgentConversationWorkspaceController conversationWorkspaceController)
{
    private readonly AgentConversationTurnController conversationTurnController = conversationTurnController;
    private readonly AgentConversationWorkspaceController conversationWorkspaceController = conversationWorkspaceController;

    public AgentConversationViewModel Create(IAgentConversationDeleteHandler conversationDeleteHandler)
    {
        return new(conversationDeleteHandler, conversationTurnController, conversationWorkspaceController);
    }

    public AgentConversationViewModel Create(AgentConversation data, IAgentConversationDeleteHandler conversationDeleteHandler)
    {
        AgentConversationViewModel conversation = Create(conversationDeleteHandler);
        conversation.Id = data.Id;
        conversation.Title = data.Title;
        conversation.CreatedAt = data.CreatedAt;
        conversation.UpdatedAt = data.UpdatedAt;
        conversation.Runtime.SerializedSessionState = data.SerializedSessionState?.Clone();
        conversation.ExternalDirectories = [.. data.ExternalDirectories];
        conversation.Messages = new(data.Messages);
        conversation.ToolApprovalRequest = data.ToolApprovalRequest;
        conversation.UpdateConversationStatistics();
        return conversation;
    }
}
