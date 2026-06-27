using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Snap.Nicole.Services.AI.Agent.Workspace.EditTool;

namespace Snap.Nicole.UI.Xaml.Controls.ChatElements;

[GeneratedDependencyProperty<object>("ContextValue")]
[GeneratedDependencyProperty<object>("AdditionValue")]
[GeneratedDependencyProperty<object>("DeletionValue")]
internal sealed partial class ChatEditToolHunkLineKindToObjectConverter : DependencyObject, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not AgentWorkspaceEditToolHunkLineKind kind)
        {
            return DependencyProperty.UnsetValue;
        }

        return kind switch
        {
            AgentWorkspaceEditToolHunkLineKind.Context => ContextValue,
            AgentWorkspaceEditToolHunkLineKind.Addition => AdditionValue,
            AgentWorkspaceEditToolHunkLineKind.Deletion => DeletionValue,
            _ => DependencyProperty.UnsetValue,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
