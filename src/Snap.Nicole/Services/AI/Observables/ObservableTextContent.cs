using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;

namespace Snap.Nicole.Services.AI.Observables;

internal sealed partial class ObservableTextContent : ObservableAIContent
{
    [ObservableProperty]
    public partial string Text { get; set; }

    public static ObservableTextContent Create(TextContent textContent)
    {
        return new()
        {
            Text = textContent.Text,
        };
    }

    public static ObservableTextContent Create(string text)
    {
        return new()
        {
            Text = text,
        };
    }

    public void Update(ObservableTextContent textContent)
    {
        Text += textContent.Text;
    }
}
