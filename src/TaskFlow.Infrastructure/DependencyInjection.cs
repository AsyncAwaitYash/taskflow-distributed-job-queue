using TaskFlow.Application.Jobs;
using TaskFlow.Infrastructure.Messaging;
using TaskFlow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaskFlow.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "TaskFlow";
    public const string RabbitMqConnectionStringName = "RabbitMq";

    public static TaskFlowInfrastructureStatus AddTaskFlowInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string clientName = "taskflow")
    {
        bool databaseConfigured = AddPersistence(services, configuration);
        bool messagingConfigured = AddMessaging(services, configuration, clientName);

        if (databaseConfigured && messagingConfigured)
        {
            services.AddScoped<JobSubmissionService>();
        }

        TaskFlowInfrastructureStatus status = new(databaseConfigured, messagingConfigured);
        services.AddSingleton(status);
        return status;
    }

    /// <summary>
    /// Registers the RabbitMQ consumer that runs jobs. Call after <see cref="AddTaskFlowInfrastructure"/>.
    /// </summary>
    public static IServiceCollection AddTaskFlowJobConsumer(
        this IServiceCollection services,
        IConfiguration configuration,
        TaskFlowInfrastructureStatus infrastructure)
    {
        if (!infrastructure.DatabaseConfigured)
        {
            throw new InvalidOperationException(
                $"The worker needs ConnectionStrings:{ConnectionStringName}.");
        }

        if (!infrastructure.MessagingConfigured)
        {
            throw new InvalidOperationException(
                $"The worker needs ConnectionStrings:{RabbitMqConnectionStringName}.");
        }

        services.AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .Validate(
                options => options.PrefetchCount is >= 1 and <= 50
                    && options.DatabaseRetryDelay > TimeSpan.Zero
                    && options.ConnectRetryDelay > TimeSpan.Zero,
                "TaskFlow worker settings are invalid.")
            .ValidateOnStart();

        services.AddScoped<JobProcessor>();
        services.AddHostedService<RabbitMqJobConsumer>();
        return services;
    }

    private static bool AddPersistence(IServiceCollection services, IConfiguration configuration)
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
        services.AddScoped<JobQueryService>();
        return true;
    }

    private static bool AddMessaging(IServiceCollection services, IConfiguration configuration, string clientName)
    {
        string? connectionString = configuration.GetConnectionString(RabbitMqConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != "amqp" && uri.Scheme != "amqps"))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{RabbitMqConnectionStringName} must be an amqp:// or amqps:// URI.");
        }

        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(
                options => options.PublishTimeout > TimeSpan.Zero && options.PublishTimeout <= TimeSpan.FromMinutes(1),
                "TaskFlow RabbitMQ settings are invalid.")
            .ValidateOnStart();

        services.AddSingleton(new RabbitMqEndpoint(uri, clientName));
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IJobPublisher, RabbitMqJobPublisher>();
        return true;
    }
}

public sealed record TaskFlowInfrastructureStatus(bool DatabaseConfigured, bool MessagingConfigured);
