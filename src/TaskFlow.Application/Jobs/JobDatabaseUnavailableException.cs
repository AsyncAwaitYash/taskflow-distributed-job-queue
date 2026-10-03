namespace TaskFlow.Application.Jobs;

public sealed class JobDatabaseUnavailableException : Exception
{
    public JobDatabaseUnavailableException(Exception innerException)
        : base("SQL Server is unavailable.", innerException)
    {
    }
}
