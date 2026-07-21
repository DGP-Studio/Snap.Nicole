using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Snap.Nicole.Services.AI.Agent.Workspace.StructuredPatch;

namespace Snap.Nicole.UI.Xaml.Controls.ChatElements;

[GeneratedDependencyProperty<object>("ContextValue")]
[GeneratedDependencyProperty<object>("AdditionValue")]
[GeneratedDependencyProperty<object>("DeletionValue")]
internal sealed partial class ChatEditToolHunkLineKindToObjectConverter : DependencyObject, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not AgentWorkspaceStructuredPatchHunkLineKind kind)
        {
            return DependencyProperty.UnsetValue;
        }

        return kind switch
        {
            AgentWorkspaceStructuredPatchHunkLineKind.Context => ContextValue,
            AgentWorkspaceStructuredPatchHunkLineKind.Addition => AdditionValue,
            AgentWorkspaceStructuredPatchHunkLineKind.Deletion => DeletionValue,
            _ => DependencyProperty.UnsetValue,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
