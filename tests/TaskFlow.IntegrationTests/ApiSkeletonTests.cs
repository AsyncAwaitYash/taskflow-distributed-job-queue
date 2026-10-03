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
    public async Task Get_root_reports_phase_and_unconfigured_dependencies()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TaskFlow", body.GetProperty("name").GetString());
        Assert.Equal("running", body.GetProperty("status").GetString());
        Assert.Equal(3, body.GetProperty("phase").GetInt32());
        Assert.False(body.GetProperty("jobProcessing").GetBoolean());
        Assert.False(body.GetProperty("databaseConfigured").GetBoolean());
        Assert.False(body.GetProperty("messagingConfigured").GetBoolean());
        Assert.Contains("not configured", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Swagger_document_lists_the_job_routes()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/jobs", out _));
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/jobs/{id}", out _));
    }

    [Fact]
    public async Task Job_routes_report_that_sql_server_is_not_configured()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/jobs");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SQL Server is not configured.", body.GetProperty("title").GetString());
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
