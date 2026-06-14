using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Snap.Nicole.UI.Xaml.Controls.ChatElements;

[GeneratedDependencyProperty<object>("PendingValue")]
[GeneratedDependencyProperty<object>("ApprovedValue")]
[GeneratedDependencyProperty<object>("RejectedValue")]
internal sealed partial class ChatToolApprovalResultToObjectConverter : DependencyObject, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (value switch
        {
            true => ApprovedValue,
            false => RejectedValue,
            _ => PendingValue,
        }) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
