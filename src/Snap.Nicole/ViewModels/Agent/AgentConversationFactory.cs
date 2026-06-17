using Snap.Nicole.Services.AI.Agent.Models;

namespace Snap.Nicole.ViewModels.Agent;

internal static class AgentConversationFactory
{
    public static AgentConversation Create(AgentConversationViewModel conversation)
    {
        return new()
        {
            Id = conversation.Id,
            Title = conversation.Title,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            ModelProviderProfileId = conversation.ModelProviderProfile?.Id,
            ModelProfileId = conversation.ModelProfile?.Id,
            SerializedSessionState = conversation.Runtime.SerializedSessionState?.Clone(),
            ExternalDirectories = [.. conversation.ExternalDirectories],
            ToolApprovalRequest = conversation.ToolApprovalRequest,
            Messages = new(conversation.Messages),
        };
    }
}
