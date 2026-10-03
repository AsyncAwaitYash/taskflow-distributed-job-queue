using TaskFlow.Application.Jobs;
using TaskFlow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaskFlow.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "TaskFlow";

    public static bool AddTaskFlowInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        services.AddDbContext<TaskFlowDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(TaskFlowDbContext).Assembly.GetName().Name)));

        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<JobSubmissionService>();
        services.AddScoped<JobQueryService>();
        return true;
    }
}
