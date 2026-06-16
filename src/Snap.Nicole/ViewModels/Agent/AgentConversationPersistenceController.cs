using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Models;
using System.Collections.Generic;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationPersistenceController(IAgentConversationProvider conversationProvider, AgentWorkspaceProvider workspaceProvider)
{
    private readonly IAgentConversationProvider conversationProvider = conversationProvider;
    private readonly AgentWorkspaceProvider workspaceProvider = workspaceProvider;

    public IEnumerable<AgentConversation> LoadConversations()
    {
        return conversationProvider.LoadConversations();
    }

    public void SaveConversation(AgentConversationViewModel conversation)
    {
        conversationProvider.SaveConversation(AgentConversationFactory.Create(conversation));
    }

    public void DeleteConversation(AgentConversationViewModel conversation)
    {
        conversationProvider.DeleteConversation(conversation.Id);
        workspaceProvider.DeleteAppManagedWorkspace(conversation.Id);
    }
}
