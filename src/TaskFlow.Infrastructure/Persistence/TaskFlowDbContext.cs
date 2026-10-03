using TaskFlow.Domain.Jobs;

using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class TaskFlowDbContext : DbContext
{
    public TaskFlowDbContext(DbContextOptions<TaskFlowDbContext> options)
        : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobAttempt> JobAttempts => Set<JobAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskFlowDbContext).Assembly);
    }
}
