using TaskFlow.Infrastructure.Persistence;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Testcontainers.MsSql;

namespace TaskFlow.IntegrationTests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    static SqlServerFixture()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
    }

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    // The container's default connection string points at master. Migrate creates this database instead.
    public string ConnectionString => new SqlConnectionStringBuilder(_container.GetConnectionString())
    {
        InitialCatalog = "TaskFlow"
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using TaskFlowDbContext db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public TaskFlowDbContext CreateContext()
    {
        DbContextOptions<TaskFlowDbContext> options = new DbContextOptionsBuilder<TaskFlowDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new TaskFlowDbContext(options);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection
    : ICollectionFixture<SqlServerFixture>, ICollectionFixture<RabbitMqFixture>
{
    public const string Name = "infrastructure";
}
