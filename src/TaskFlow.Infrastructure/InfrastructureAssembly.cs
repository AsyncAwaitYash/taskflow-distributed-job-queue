using TaskFlow.Application;
using TaskFlow.Domain;

namespace TaskFlow.Infrastructure;

/// <summary>
/// Identifies the Infrastructure assembly. SQL Server and RabbitMQ arrive in later phases.
/// </summary>
public static class InfrastructureAssembly
{
    public static readonly string Name = "TaskFlow.Infrastructure";

    public static string ApplicationLayer => ApplicationAssembly.Name;

    public static string DomainLayer => DomainAssembly.Name;
}
