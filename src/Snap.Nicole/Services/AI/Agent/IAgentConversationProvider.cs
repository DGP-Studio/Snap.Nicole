using Snap.Nicole.Services.AI.Agent.Models;
using System.Collections.Generic;

namespace Snap.Nicole.Services.AI.Agent;

internal interface IAgentConversationProvider
{
    IReadOnlyList<AgentConversation> LoadConversations();

    void SaveConversation(AgentConversation conversation);

    void DeleteConversation(Guid id);
}
