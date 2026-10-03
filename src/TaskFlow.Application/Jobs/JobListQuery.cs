using TaskFlow.Domain.Jobs;

namespace TaskFlow.Application.Jobs;

public sealed record JobListQuery(
    JobStatus? Status,
    string? Type,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo,
    int Page,
    int PageSize);

public sealed record JobPage(IReadOnlyList<Job> Items, int Page, int PageSize, int TotalCount);
