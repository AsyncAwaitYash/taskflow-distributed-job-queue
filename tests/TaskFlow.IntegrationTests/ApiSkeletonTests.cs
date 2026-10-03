using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace TaskFlow.IntegrationTests;

public sealed class ApiSkeletonTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiSkeletonTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_root_returns_phase_zero_skeleton()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TaskFlow", body.GetProperty("name").GetString());
        Assert.Equal("skeleton", body.GetProperty("status").GetString());
        Assert.Equal(0, body.GetProperty("phase").GetInt32());
        Assert.False(body.GetProperty("jobProcessing").GetBoolean());
        Assert.Contains("not implemented", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Api_assembly_references_application_and_infrastructure()
    {
        HashSet<string> referenced = typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("TaskFlow.Application", referenced);
        Assert.Contains("TaskFlow.Infrastructure", referenced);
    }
}
