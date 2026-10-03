namespace TaskFlow.Application.Jobs;

public sealed class InvalidJobRequestException : Exception
{
    public InvalidJobRequestException(IReadOnlyDictionary<string, string[]> errors)
        : base("The job request is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
