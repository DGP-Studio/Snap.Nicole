using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Snap.Nicole.Services.AI.Agent.Workspace.WriteTool;

namespace Snap.Nicole.UI.Xaml.Controls.ChatElements;

[GeneratedDependencyProperty<object>("CreateValue")]
[GeneratedDependencyProperty<object>("UpdateValue")]
internal sealed partial class ChatWriteToolResultTypeToObjectConverter : DependencyObject, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not AgentWorkspaceWriteToolResultType type)
        {
            return DependencyProperty.UnsetValue;
        }

        return type switch
        {
            AgentWorkspaceWriteToolResultType.Create => CreateValue,
            AgentWorkspaceWriteToolResultType.Update => UpdateValue,
            _ => DependencyProperty.UnsetValue,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
