using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.IO;
using Snap.Nicole.Core.Text.Json;
using Snap.Nicole.Services.AI.Agent.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Snap.Nicole.Services.AI.Agent;

internal sealed class AgentConversationFileProvider(IServiceProvider serviceProvider) : IAgentConversationProvider
{
    private static readonly string DirectoryFullPath = Path.Combine(WellKnownLocations.Settings, "AgentConversations");

    private readonly JsonSerializerOptions jsonOptions = serviceProvider.GetRequiredKeyedService<JsonSerializerOptions>(JsonSerializerOptionsKey.AgentConversation);

    public IReadOnlyList<AgentConversation> LoadConversations()
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AgentConversationLoad, "Load agent conversation");

        if (!Directory.Exists(DirectoryFullPath))
        {
            return [];
        }

        List<AgentConversation> conversations = [];
        foreach (string filePath in Directory.EnumerateFiles(DirectoryFullPath, "*.json"))
        {
            try
            {
                using (FileStream stream = File.OpenRead(filePath))
                {
                    if (JsonSerializer.Deserialize<AgentConversation>(stream, jsonOptions) is { } conversation)
                    {
                        conversations.Add(conversation);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                span.SetData(SentryData.AgentConversationFile, filePath);
                SentryDiagnostics.CaptureException(ex, span, SentryOperations.AgentConversationLoad);
            }
        }

        return conversations;
    }

    public void SaveConversation(AgentConversation conversation)
    {
        Directory.CreateDirectory(DirectoryFullPath);

        string filePath = GetConversationFilePath(conversation.Id);
        string tempFilePath = $"{filePath}.tmp";

        using (FileStream stream = File.Open(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, conversation, jsonOptions);
        }

        File.Move(tempFilePath, filePath, true);
    }

    public void DeleteConversation(Guid id)
    {
        string filePath = GetConversationFilePath(id);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static string GetConversationFilePath(Guid id)
    {
        return Path.Combine(DirectoryFullPath, $"{id:N}.json");
    }
}
