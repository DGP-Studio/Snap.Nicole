using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snap.Nicole.Services.AI.Observables;

namespace Snap.Nicole.UI.Xaml.Controls.ChatElements;

internal sealed class ChatContentTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }

    public DataTemplate? ReasoningTemplate { get; set; }

    public DataTemplate? FunctionCallTemplate { get; set; }

    public DataTemplate? FunctionResultTemplate { get; set; }

    public DataTemplate? ToolApprovalRequestTemplate { get; set; }

    public DataTemplate? UsageTemplate { get; set; }

    public DataTemplate? ErrorTemplate { get; set; }

    public DataTemplate? FallbackTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item switch
        {
            ObservableTextContent => TextTemplate,
            ObservableTextReasoningContent => ReasoningTemplate,
            ObservableFunctionCallContent => FunctionCallTemplate,
            ObservableFunctionResultContent => FunctionResultTemplate,
            ObservableToolApprovalRequestContent => ToolApprovalRequestTemplate,
            ObservableUsageContent => UsageTemplate,
            ObservableErrorContent => ErrorTemplate,
            _ => FallbackTemplate,
        };
    }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        return SelectTemplateCore(item);
    }
}
