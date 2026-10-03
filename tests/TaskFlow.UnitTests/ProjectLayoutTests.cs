using TaskFlow.Application;
using TaskFlow.Domain;
using TaskFlow.Infrastructure;

namespace TaskFlow.UnitTests;

public sealed class ProjectLayoutTests
{
    [Fact]
    public void Domain_assembly_has_no_taskflow_dependencies()
    {
        string[] referenced = ReferencedTaskFlowAssemblies(typeof(DomainAssembly));

        Assert.Equal(DomainAssembly.Name, typeof(DomainAssembly).Assembly.GetName().Name);
        Assert.Empty(referenced);
    }

    [Fact]
    public void Application_references_domain_and_not_infrastructure()
    {
        string[] referenced = ReferencedTaskFlowAssemblies(typeof(ApplicationAssembly));

        Assert.Equal([DomainAssembly.Name], referenced);
    }

    [Fact]
    public void Infrastructure_references_application_and_domain_only()
    {
        string[] referenced = ReferencedTaskFlowAssemblies(typeof(InfrastructureAssembly));

        Assert.Equal(
            [ApplicationAssembly.Name, DomainAssembly.Name],
            referenced.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    private static string[] ReferencedTaskFlowAssemblies(Type marker)
    {
        return marker.Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("TaskFlow.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
