using TaskFlow.Api.Jobs;
using TaskFlow.Application;
using TaskFlow.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "TaskFlow",
        Version = "v1",
        Description = "Stores background jobs in SQL Server. New jobs stay Pending because RabbitMQ is not connected yet."
    });
});
builder.Services.AddTaskFlowApplication(builder.Configuration);
bool databaseConfigured = builder.Services.AddTaskFlowInfrastructure(builder.Configuration);

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "TaskFlow v1");
    options.RoutePrefix = "swagger";
});

app.Logger.LogInformation(
    "TaskFlow API. DatabaseConfigured={DatabaseConfigured} JobProcessingEnabled={JobProcessingEnabled}",
    databaseConfigured,
    false);

app.MapGet("/", () => Results.Ok(new
{
    name = "TaskFlow",
    status = "running",
    phase = 2,
    jobProcessing = false,
    databaseConfigured,
    message = databaseConfigured
        ? "Jobs are stored in SQL Server and stay Pending. RabbitMQ and job handlers are not implemented."
        : "SQL Server is not configured, so job routes return 503. RabbitMQ and job handlers are not implemented."
}));

app.MapJobEndpoints();

app.Run();

public partial class Program;
