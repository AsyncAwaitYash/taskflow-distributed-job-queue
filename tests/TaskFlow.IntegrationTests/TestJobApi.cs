using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskFlow.IntegrationTests;

internal static class TestJobApi
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    public static async Task<Guid> SubmitAsync(HttpClient client, string type, object payload, int? maxAttempts = null)
    {
        object body = maxAttempts is null ? new { type, payload } : new { type, payload, maxAttempts };
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/jobs", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> GetJobAsync(HttpClient client, Guid jobId)
    {
        return await client.GetFromJsonAsync<JsonElement>($"/api/v1/jobs/{jobId}");
    }

    public static async Task<JsonElement> WaitForStatusAsync(HttpClient client, Guid jobId, string status)
    {
        DateTime deadline = DateTime.UtcNow + WaitLimit;
        JsonElement job;
        do
        {
            job = await GetJobAsync(client, jobId);
            if (job.GetProperty("status").GetString() == status)
            {
                return job;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Job {jobId} stayed {job.GetProperty("status").GetString()} instead of reaching {status}.");
        return job;
    }
}
