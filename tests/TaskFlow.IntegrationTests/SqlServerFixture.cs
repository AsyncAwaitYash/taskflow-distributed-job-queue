using TaskFlow.Infrastructure.Persistence;

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

    public string ConnectionString => _container.GetConnectionString();

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
