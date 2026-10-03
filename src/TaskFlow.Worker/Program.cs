using TaskFlow.Application;
using TaskFlow.Infrastructure;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTaskFlowApplication(builder.Configuration);
TaskFlowInfrastructureStatus infrastructure = builder.Services.AddTaskFlowInfrastructure(builder.Configuration, "taskflow-worker");
builder.Services.AddTaskFlowJobConsumer(builder.Configuration, infrastructure);

IHost host = builder.Build();
host.Run();
