using System.Text.Json;

namespace TaskFlow.Api.Jobs;

public sealed class CreateJobRequest
{
    public string? Type { get; set; }

    public JsonElement Payload { get; set; }

    public int? MaxAttempts { get; set; }

    public string? CorrelationId { get; set; }
}
