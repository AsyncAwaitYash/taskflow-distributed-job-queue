namespace TaskFlow.Application.Jobs;

/// <summary>
/// The queue message body. The payload stays in SQL Server.
/// </summary>
public sealed record JobMessage(Guid JobId, string Type, string CorrelationId);
