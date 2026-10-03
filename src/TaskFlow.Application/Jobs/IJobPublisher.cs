namespace TaskFlow.Application.Jobs;

/// <summary>
/// Sends a job reference to the queue. Returns only after the broker has confirmed the message.
/// Throws <see cref="JobPublishFailedException"/> when the message may not have been accepted.
/// </summary>
public interface IJobPublisher
{
    Task PublishAsync(JobMessage message, CancellationToken cancellationToken);
}
