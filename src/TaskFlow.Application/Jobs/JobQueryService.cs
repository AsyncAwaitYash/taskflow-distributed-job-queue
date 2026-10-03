using TaskFlow.Domain.Jobs;

namespace TaskFlow.Application.Jobs;

public sealed class JobQueryService
{
    public const int MaxPageSize = 100;

    private readonly IJobRepository _repository;

    public JobQueryService(IJobRepository repository)
    {
        _repository = repository;
    }

    public Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return _repository.GetAsync(id, cancellationToken);
    }

    public Task<JobPage> ListAsync(JobListQuery query, CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> errors = new(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["Page must be at least 1."];
        }

        if (query.PageSize < 1 || query.PageSize > MaxPageSize)
        {
            errors["pageSize"] = [$"Page size must be from 1 to {MaxPageSize}."];
        }

        if (query.CreatedFrom is not null
            && query.CreatedTo is not null
            && query.CreatedFrom > query.CreatedTo)
        {
            errors["createdFrom"] = ["createdFrom cannot be later than createdTo."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidJobRequestException(errors);
        }

        return _repository.ListAsync(query, cancellationToken);
    }
}
