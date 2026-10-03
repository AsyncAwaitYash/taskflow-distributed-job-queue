using TaskFlow.Application.Jobs;
using TaskFlow.Application.Jobs.Handlers;

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

        services.AddSingleton<IJobHandler, DemoSuccessHandler>();
        services.AddSingleton<IJobHandler, DemoPermanentFailureHandler>();
        services.AddSingleton<IJobHandler, DemoSlowHandler>();
        services.AddSingleton<IJobHandler, EmailSendHandler>();
        services.AddSingleton<IJobHandler, ReportGenerateHandler>();
        services.AddSingleton<IJobHandler, DataProcessHandler>();
        services.AddSingleton<JobHandlerRegistry>();

        return services;
    }
}
