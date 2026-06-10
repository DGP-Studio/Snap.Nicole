using Snap.Nicole.Core.ObjectModel.Input;

namespace Snap.Nicole.ViewModels.Agent;

internal sealed class AgentConversationCommandNotifier(AgentConversationViewModel conversation)
{
    private readonly AgentConversationViewModel conversation = conversation;

    public void NotifyStopGenerationChanged()
    {
        conversation.StopGenerationCommand.NotifyCanExecuteChanged();
    }

    public void NotifyDeleteConversationChanged()
    {
        conversation.DeleteConversationCommand.NotifyCanExecuteChanged();
    }

    public void NotifyToolApprovalChanged()
    {
        RelayCommands.NotifyCanExecuteChanged(conversation.ApproveToolApprovalCommand, conversation.DenyToolApprovalCommand);
    }

    public void NotifyTurnChanged(bool notifyToolApprovalCommands)
    {
        RelayCommands.NotifyCanExecuteChanged(conversation.SendMessageCommand, conversation.StopGenerationCommand);

        if (notifyToolApprovalCommands)
        {
            NotifyToolApprovalChanged();
        }
    }

    public void NotifyGenerationCommandChanged()
    {
        RelayCommands.NotifyCanExecuteChanged(conversation.SendMessageCommand, conversation.StopGenerationCommand, conversation.DeleteConversationCommand, conversation.ApproveToolApprovalCommand, conversation.DenyToolApprovalCommand);
    }
}
