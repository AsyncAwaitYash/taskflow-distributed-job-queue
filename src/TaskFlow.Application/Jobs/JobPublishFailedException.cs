namespace TaskFlow.Application.Jobs;

public sealed class JobPublishFailedException : Exception
{
    public JobPublishFailedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
