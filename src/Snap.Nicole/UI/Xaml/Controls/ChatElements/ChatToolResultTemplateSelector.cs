using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snap.Nicole.Services.AI.Observables;
using Snap.Nicole.Services.AI.Observables.BuiltInTools;

namespace Snap.Nicole.UI.Xaml.Controls.ChatElements;

internal sealed class ChatToolResultTemplateSelector : DataTemplateSelector
{
    public DataTemplate? CodeInterpreterTemplate { get; set; }

    public DataTemplate? EditTemplate { get; set; }

    public DataTemplate? FunctionTemplate { get; set; }

    public DataTemplate? ImageGenerationTemplate { get; set; }

    public DataTemplate? McpServerTemplate { get; set; }

    public DataTemplate? WebSearchTemplate { get; set; }

    public DataTemplate? FallbackTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item switch
        {
            ObservableCodeInterpreterToolResultContent => CodeInterpreterTemplate,
            ObservableEditToolResultContent => EditTemplate,
            ObservableFunctionResultContent => FunctionTemplate,
            ObservableImageGenerationToolResultContent => ImageGenerationTemplate,
            ObservableMcpServerToolResultContent => McpServerTemplate,
            ObservableWebSearchToolResultContent => WebSearchTemplate,
            _ => FallbackTemplate,
        };
    }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        return SelectTemplateCore(item);
    }
}
