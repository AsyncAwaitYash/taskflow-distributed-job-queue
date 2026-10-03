using System.Text.Json;

namespace TaskFlow.Application.Jobs.Handlers;

internal static class JobPayload
{
    public const string InvalidPayload = "InvalidPayload";

    public static JsonElement Parse(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }
}
