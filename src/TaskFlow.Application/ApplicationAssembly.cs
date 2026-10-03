using TaskFlow.Domain;

namespace TaskFlow.Application;

/// <summary>
/// Identifies the Application assembly. Use cases arrive in later phases.
/// </summary>
public static class ApplicationAssembly
{
    public static readonly string Name = "TaskFlow.Application";

    public static string DomainLayer => DomainAssembly.Name;
}
