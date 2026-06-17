using Snap.Nicole.Resources;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentConversationException : Exception
{
    public AgentConversationException(string message)
        : base(message)
    {
    }

    public AgentConversationException(SRName message)
        : base(StringResourceProxy.Default[message])
    {
        ResourceName = message;
    }

    public AgentConversationException(SRName message, Exception innerException)
        : base(StringResourceProxy.Default[message], innerException)
    {
        ResourceName = message;
    }

    public SRName? ResourceName { get; }
}
