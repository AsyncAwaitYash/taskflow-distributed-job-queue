using TaskFlow.Application.Jobs;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaskFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTaskFlowApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<JobSubmissionOptions>()
            .Bind(configuration.GetSection(JobSubmissionOptions.SectionName))
            .Validate(
                options => options.DefaultMaxAttempts >= 1
                    && options.MaxAllowedAttempts >= options.DefaultMaxAttempts
                    && options.MaxAllowedAttempts <= 100,
                "TaskFlow attempt settings are invalid.")
            .ValidateOnStart();

        return services;
    }
}
