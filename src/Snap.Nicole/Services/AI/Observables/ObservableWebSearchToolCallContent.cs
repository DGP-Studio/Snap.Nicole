using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using System.Collections.ObjectModel;

namespace Snap.Nicole.Services.AI.Observables;

internal sealed partial class ObservableWebSearchToolCallContent : ObservableToolCallContent
{
    [ObservableProperty]
    public partial ObservableCollection<string>? Queries { get; set; }

    public static ObservableWebSearchToolCallContent Create(WebSearchToolCallContent toolCallContent)
    {
        return new()
        {
            CallId = toolCallContent.CallId,
            Queries = toolCallContent.Queries is null ? null : new ObservableCollection<string>(toolCallContent.Queries),
        };
    }
}
