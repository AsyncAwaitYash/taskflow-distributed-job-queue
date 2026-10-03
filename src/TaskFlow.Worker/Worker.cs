using TaskFlow.Application;
using TaskFlow.Infrastructure;

namespace TaskFlow.Worker;

/// <summary>
/// Keeps the worker process alive. It does not consume a queue.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "TaskFlow worker skeleton started. Application={ApplicationLayer} Infrastructure={InfrastructureLayer} JobProcessingEnabled={JobProcessingEnabled}",
            ApplicationAssembly.Name,
            InfrastructureAssembly.Name,
            false);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("TaskFlow worker skeleton is stopping.");
        }
    }
}
