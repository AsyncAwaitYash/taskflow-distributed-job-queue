using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TaskFlow.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class JobApiTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public JobApiTests(SqlServerFixture sql)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:TaskFlow", sql.ConnectionString);
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Post_stores_a_pending_job_that_can_be_read_back()
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/v1/jobs", new
        {
            type = "demo.success",
            payload = new { message = "hello" },
            correlationId = "api-create"
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        JsonElement body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal("demo.success", body.GetProperty("type").GetString());
        Assert.Equal("api-create", body.GetProperty("correlationId").GetString());
        Assert.Equal(5, body.GetProperty("maxAttempts").GetInt32());
        Assert.Equal("hello", body.GetProperty("payload").GetProperty("message").GetString());
        Assert.Equal(0, body.GetProperty("attempts").GetArrayLength());
        Assert.Equal($"/api/v1/jobs/{body.GetProperty("id").GetGuid()}", created.Headers.Location?.OriginalString);

        HttpResponseMessage fetched = await _client.GetAsync($"/api/v1/jobs/{body.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        JsonElement stored = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", stored.GetProperty("status").GetString());
        Assert.Equal("hello", stored.GetProperty("payload").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Post_rejects_an_unknown_type()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/jobs", new
        {
            type = "not.a.job",
            payload = new { message = "hello" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_unknown_id_returns_not_found()
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/v1/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_filters_by_type_and_pages_the_matches()
    {
        for (int index = 0; index < 3; index++)
        {
            HttpResponseMessage created = await _client.PostAsJsonAsync("/api/v1/jobs", new
            {
                type = "demo.permanent-failure",
                payload = new { index }
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        HttpResponseMessage page = await _client.GetAsync("/api/v1/jobs?type=demo.permanent-failure&page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        JsonElement body = await page.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal("demo.permanent-failure", body.GetProperty("items")[0].GetProperty("type").GetString());

        DateTimeOffset first = body.GetProperty("items")[0].GetProperty("createdAt").GetDateTimeOffset();
        DateTimeOffset second = body.GetProperty("items")[1].GetProperty("createdAt").GetDateTimeOffset();
        Assert.True(first >= second);
    }

    [Fact]
    public async Task List_filters_by_created_time()
    {
        DateTimeOffset createdFrom = DateTimeOffset.UtcNow.AddMinutes(-1);
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/v1/jobs", new
        {
            type = "report.generate",
            payload = new { name = "daily" }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        string from = Uri.EscapeDataString(createdFrom.ToString("o"));
        string future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddYears(1).ToString("o"));

        HttpResponseMessage match = await _client.GetAsync($"/api/v1/jobs?type=report.generate&createdFrom={from}");
        JsonElement matched = await match.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(matched.GetProperty("totalCount").GetInt32() >= 1);

        HttpResponseMessage none = await _client.GetAsync($"/api/v1/jobs?type=report.generate&createdFrom={future}");
        JsonElement empty = await none.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_rejects_a_page_size_above_the_cap()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/v1/jobs?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
