using TaskFlow.Domain.Jobs;

namespace TaskFlow.Application.Jobs;

public interface IJobRepository
{
    Task AddAsync(Job job, CancellationToken cancellationToken);

    Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<JobPage> ListAsync(JobListQuery query, CancellationToken cancellationToken);
}
