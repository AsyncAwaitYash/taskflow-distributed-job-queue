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
        Description = "Stores background jobs in SQL Server and publishes them to RabbitMQ. A job is Queued only after the broker confirms. A separate worker process runs it."
    });
});
builder.Services.AddTaskFlowApplication(builder.Configuration);
TaskFlowInfrastructureStatus infrastructure = builder.Services.AddTaskFlowInfrastructure(builder.Configuration, "taskflow-api");

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "TaskFlow v1");
    options.RoutePrefix = "swagger";
});

app.Logger.LogInformation(
    "TaskFlow API. DatabaseConfigured={DatabaseConfigured} MessagingConfigured={MessagingConfigured} JobProcessingEnabled={JobProcessingEnabled}",
    infrastructure.DatabaseConfigured,
    infrastructure.MessagingConfigured,
    false);

app.MapGet("/", () => Results.Ok(new
{
    name = "TaskFlow",
    status = "running",
    phase = 3,
    jobProcessing = false,
    databaseConfigured = infrastructure.DatabaseConfigured,
    messagingConfigured = infrastructure.MessagingConfigured,
    message = (infrastructure.DatabaseConfigured, infrastructure.MessagingConfigured) switch
    {
        (true, true) => "Jobs are stored in SQL Server and published to RabbitMQ. A separate TaskFlow.Worker process runs them.",
        (true, false) => "RabbitMQ is not configured, so job submission returns 503. List and get work.",
        _ => "SQL Server is not configured, so job routes return 503."
    }
}));

app.MapJobEndpoints();

app.Run();

public partial class Program;
