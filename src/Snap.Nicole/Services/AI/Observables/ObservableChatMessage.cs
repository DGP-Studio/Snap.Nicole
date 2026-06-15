using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Text.Json;

namespace Snap.Nicole.Services.AI.Observables;

internal sealed partial class ObservableChatMessage : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string? AuthorName { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? CreatedAt { get; set; }

    [ObservableProperty]
    public partial ChatRole Role { get; set; }

    [ObservableProperty]
    public partial ObservableAIContentCollection Contents { get; set; } = new();

    [ObservableProperty]
    public partial string? MessageId { get; set; }

    public static ObservableChatMessage Create(ChatMessage chatMessage, JsonSerializerOptions jsonOptions)
    {
        return new()
        {
            Role = chatMessage.Role,
            CreatedAt = chatMessage.CreatedAt,
            AuthorName = chatMessage.AuthorName,
            MessageId = chatMessage.MessageId,
            Contents = ObservableAIContentCollection.Create(chatMessage.Contents, jsonOptions) ?? [],
        };
    }

    public static ObservableChatMessage Create(ChatRole role, DateTimeOffset? createdAt, string? authorName = default, ObservableAIContent? content = default)
    {
        ObservableChatMessage observableChatMessage = new()
        {
            Role = role,
            CreatedAt = createdAt,
            AuthorName = authorName,
        };

        observableChatMessage.Contents.AddOrUpdate(content);
        return observableChatMessage;
    }
}
