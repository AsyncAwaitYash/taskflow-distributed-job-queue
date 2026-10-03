using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build migrations without a running SQL Server.
/// The connection string is not used to connect during <c>migrations add</c>.
/// </summary>
public sealed class TaskFlowDbContextFactory : IDesignTimeDbContextFactory<TaskFlowDbContext>
{
    public TaskFlowDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<TaskFlowDbContext> options = new DbContextOptionsBuilder<TaskFlowDbContext>()
            .UseSqlServer("Server=localhost;Database=TaskFlow;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new TaskFlowDbContext(options);
    }
}
