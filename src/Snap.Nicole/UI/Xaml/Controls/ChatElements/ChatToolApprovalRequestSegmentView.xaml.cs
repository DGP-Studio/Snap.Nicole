using Microsoft.UI.Xaml.Controls;
using Snap.Nicole.Services.AI.Observables;
using System.Windows.Input;

namespace Snap.Nicole.UI.Xaml.Controls.ChatElements;

[GeneratedDependencyProperty<ObservableToolApprovalRequestContent>("ToolApprovalRequest")]
[GeneratedDependencyProperty<ICommand>("ApproveCommand")]
[GeneratedDependencyProperty<ICommand>("DenyCommand")]
internal sealed partial class ChatToolApprovalRequestSegmentView : UserControl
{
    public ChatToolApprovalRequestSegmentView()
    {
        InitializeComponent();
    }
}
