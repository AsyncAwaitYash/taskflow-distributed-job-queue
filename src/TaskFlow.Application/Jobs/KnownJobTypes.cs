namespace TaskFlow.Application.Jobs;

/// <summary>
/// Types the API accepts. Phase 3 replaces this list with the handler registry.
/// Names are case-sensitive.
/// </summary>
public static class KnownJobTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "demo.success",
        "demo.transient-failure",
        "demo.permanent-failure",
        "demo.slow",
        "email.send",
        "report.generate",
        "data.process"
    };
}
