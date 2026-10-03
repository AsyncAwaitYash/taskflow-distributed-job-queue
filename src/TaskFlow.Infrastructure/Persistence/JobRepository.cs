using TaskFlow.Application.Jobs;
using TaskFlow.Domain.Jobs;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Infrastructure.Persistence;

internal sealed class JobRepository : IJobRepository
{
    private readonly TaskFlowDbContext _db;

    public JobRepository(TaskFlowDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(Job job, CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            _db.Jobs.Add(job);
            await _db.SaveChangesAsync(cancellationToken);
        });
    }

    public Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => _db.Jobs
            .Include(job => job.Attempts.OrderBy(attempt => attempt.AttemptNumber))
            .FirstOrDefaultAsync(job => job.Id == id, cancellationToken));
    }

    public async Task<JobPage> ListAsync(JobListQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Job> jobs = _db.Jobs.AsNoTracking();

        if (query.Status is not null)
        {
            jobs = jobs.Where(job => job.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            string type = query.Type.Trim();
            jobs = jobs.Where(job => job.Type == type);
        }

        if (query.CreatedFrom is not null)
        {
            jobs = jobs.Where(job => job.CreatedAt >= query.CreatedFrom);
        }

        if (query.CreatedTo is not null)
        {
            jobs = jobs.Where(job => job.CreatedAt <= query.CreatedTo);
        }

        return await ExecuteAsync(async () =>
        {
            int totalCount = await jobs.CountAsync(cancellationToken);
            List<Job> items = await jobs
                .OrderByDescending(job => job.CreatedAt)
                .ThenByDescending(job => job.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

            return new JobPage(items, query.Page, query.PageSize, totalCount);
        });
    }

    private static async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (ContainsSqlException(exception))
        {
            throw new JobDatabaseUnavailableException(exception);
        }
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (ContainsSqlException(exception))
        {
            throw new JobDatabaseUnavailableException(exception);
        }
    }

    private static bool ContainsSqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException)
            {
                return true;
            }
        }

        return false;
    }
}
