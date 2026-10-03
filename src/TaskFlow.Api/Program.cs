using TaskFlow.Application;
using TaskFlow.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

app.Logger.LogInformation(
    "TaskFlow API skeleton. Application={ApplicationLayer} Infrastructure={InfrastructureLayer} JobProcessingEnabled={JobProcessingEnabled}",
    ApplicationAssembly.Name,
    InfrastructureAssembly.Name,
    false);

app.MapGet("/", () => Results.Ok(new
{
    name = "TaskFlow",
    status = "skeleton",
    phase = 0,
    jobProcessing = false,
    message = "Phase 0 skeleton. Job submission, RabbitMQ, and job handlers are not implemented."
}));

app.Run();

public partial class Program;
