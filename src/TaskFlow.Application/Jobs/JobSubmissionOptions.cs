namespace TaskFlow.Application.Jobs;

public sealed class JobSubmissionOptions
{
    public const string SectionName = "TaskFlow";

    public int DefaultMaxAttempts { get; set; } = 5;

    public int MaxAllowedAttempts { get; set; } = 20;
}
