namespace TaskFlow.Domain.Jobs;

internal static class DomainGuard
{
    public static string Required(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{name} must be at most {maxLength} characters.", name);
        }

        return trimmed;
    }
}
