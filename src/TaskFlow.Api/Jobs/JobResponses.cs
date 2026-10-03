using System.Text.Json;

using TaskFlow.Domain.Jobs;

namespace TaskFlow.Api.Jobs;

public sealed record JobAttemptResponse(
    Guid Id,
    int AttemptNumber,
    string WorkerId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Outcome,
    string? ErrorType,
    string? ErrorMessage,
    long? DurationMilliseconds);

public sealed record JobResponse(
    Guid Id,
    string Type,
    JsonElement Payload,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CorrelationId,
    IReadOnlyList<JobAttemptResponse> Attempts);

public sealed record JobSummaryResponse(
    Guid Id,
    string Type,
    JsonElement Payload,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset? NextAttemptAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CorrelationId);

public sealed record JobListResponse(
    IReadOnlyList<JobSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public static class JobResponseMapping
{
    public static JobResponse ToResponse(Job job)
    {
        return new JobResponse(
            job.Id,
            job.Type,
            ParsePayload(job.Payload),
            job.Status.ToString(),
            job.AttemptCount,
            job.MaxAttempts,
            job.NextAttemptAt,
            job.LastStartedAt,
            job.LastCompletedAt,
            job.LastError,
            job.CreatedAt,
            job.UpdatedAt,
            job.CorrelationId,
            job.Attempts.Select(ToAttempt).ToArray());
    }

    public static JobSummaryResponse ToSummary(Job job)
    {
        return new JobSummaryResponse(
            job.Id,
            job.Type,
            ParsePayload(job.Payload),
            job.Status.ToString(),
            job.AttemptCount,
            job.MaxAttempts,
            job.NextAttemptAt,
            job.LastError,
            job.CreatedAt,
            job.UpdatedAt,
            job.CorrelationId);
    }

    private static JobAttemptResponse ToAttempt(JobAttempt attempt)
    {
        return new JobAttemptResponse(
            attempt.Id,
            attempt.AttemptNumber,
            attempt.WorkerId,
            attempt.StartedAt,
            attempt.CompletedAt,
            attempt.Outcome?.ToString(),
            attempt.ErrorType,
            attempt.ErrorMessage,
            attempt.Duration is null ? null : (long)attempt.Duration.Value.TotalMilliseconds);
    }

    private static JsonElement ParsePayload(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }
}
