namespace TaskFlow.Application.Jobs;

public sealed record SubmitJob(string? Type, string? PayloadJson, int? MaxAttempts, string? CorrelationId);
