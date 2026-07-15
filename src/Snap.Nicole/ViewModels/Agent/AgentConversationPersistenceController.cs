using Snap.Nicole.Services.AI.Agent;
using Snap.Nicole.Services.AI.Agent.Models;
using Snap.Nicole.Services.AI.Agent.Workspace;
using System.Collections.Generic;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationPersistenceController(IAgentConversationProvider conversationProvider)
{
    private readonly IAgentConversationProvider conversationProvider = conversationProvider;

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
        AgentWorkspaceProvider.DeleteAppManagedWorkspace(conversation.Id);
    }
}
