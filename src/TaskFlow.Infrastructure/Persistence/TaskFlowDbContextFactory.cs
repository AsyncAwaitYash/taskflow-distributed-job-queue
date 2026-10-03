using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c>. <c>database update</c> connects with <c>ConnectionStrings__TaskFlow</c> from the environment.
/// <c>migrations add</c> does not connect, so the fallback string only has to be well formed.
/// </summary>
public sealed class TaskFlowDbContextFactory : IDesignTimeDbContextFactory<TaskFlowDbContext>
{
    private const string Fallback = "Server=localhost;Database=TaskFlow;Trusted_Connection=True;TrustServerCertificate=True";

    public TaskFlowDbContext CreateDbContext(string[] args)
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("ConnectionStrings__TaskFlow");
        string connectionString = string.IsNullOrWhiteSpace(fromEnvironment) ? Fallback : fromEnvironment;

        DbContextOptions<TaskFlowDbContext> options = new DbContextOptionsBuilder<TaskFlowDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TaskFlowDbContext(options);
    }
}
