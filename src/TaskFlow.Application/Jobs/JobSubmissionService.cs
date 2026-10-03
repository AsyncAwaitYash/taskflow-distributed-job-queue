using System.Text.Json;

using TaskFlow.Domain.Jobs;

using Microsoft.Extensions.Options;

namespace TaskFlow.Application.Jobs;

public sealed class JobSubmissionService
{
    private readonly IJobRepository _repository;
    private readonly TimeProvider _clock;
    private readonly JobSubmissionOptions _options;

    public JobSubmissionService(
        IJobRepository repository,
        TimeProvider clock,
        IOptions<JobSubmissionOptions> options)
    {
        _repository = repository;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Job> SubmitAsync(SubmitJob command, CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> errors = new(StringComparer.Ordinal);
        string type = command.Type?.Trim() ?? string.Empty;

        if (type.Length == 0)
        {
            errors["type"] = ["Type is required."];
        }
        else if (type.Length > JobLimits.MaxTypeLength)
        {
            errors["type"] = [$"Type must be at most {JobLimits.MaxTypeLength} characters."];
        }
        else if (!KnownJobTypes.All.Contains(type))
        {
            errors["type"] = ["Type is not a registered job type."];
        }

        string? payload = command.PayloadJson;
        if (string.IsNullOrWhiteSpace(payload))
        {
            errors["payload"] = ["Payload must be a JSON object."];
        }
        else if (payload.Length > JobLimits.MaxPayloadLength)
        {
            errors["payload"] = [$"Payload must be at most {JobLimits.MaxPayloadLength} characters."];
        }
        else if (!IsJsonObject(payload))
        {
            errors["payload"] = ["Payload must be a JSON object."];
        }

        int maxAttempts = command.MaxAttempts ?? _options.DefaultMaxAttempts;
        if (command.MaxAttempts is < 1)
        {
            errors["maxAttempts"] = ["Max attempts must be at least 1."];
        }
        else if (maxAttempts > _options.MaxAllowedAttempts)
        {
            errors["maxAttempts"] = [$"Max attempts must be at most {_options.MaxAllowedAttempts}."];
        }

        if (command.CorrelationId is { Length: > JobLimits.MaxCorrelationIdLength })
        {
            errors["correlationId"] = [$"Correlation id must be at most {JobLimits.MaxCorrelationIdLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidJobRequestException(errors);
        }

        Job job = Job.Create(type, payload!, maxAttempts, _clock.GetUtcNow(), command.CorrelationId);
        await _repository.AddAsync(job, cancellationToken);
        return job;
    }

    private static bool IsJsonObject(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
