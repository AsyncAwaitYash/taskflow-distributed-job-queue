using System.Text.Json;

namespace TaskFlow.Application.Jobs;

/// <summary>
/// Decides whether trying again could help. Anything not on the permanent list is retryable,
/// and <c>MaxAttempts</c> is what stops it.
/// </summary>
public static class JobFailureClassifier
{
    public static bool IsPermanent(Exception exception)
    {
        // Bad input fails the same way every time. ArgumentException covers its subclasses.
        return exception is JsonException or ArgumentException or FormatException or NotSupportedException;
    }
}
