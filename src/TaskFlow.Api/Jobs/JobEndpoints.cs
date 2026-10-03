using System.Text.Json;

using TaskFlow.Application.Jobs;
using TaskFlow.Domain.Jobs;
using TaskFlow.Infrastructure;

using Microsoft.AspNetCore.Mvc;

namespace TaskFlow.Api.Jobs;

public static class JobEndpoints
{
    public static RouteGroupBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/jobs")
            .WithTags("Jobs");

        group.MapPost("/", Submit)
            .WithName("CreateJob")
            .Produces<JobResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", List)
            .WithName("ListJobs")
            .Produces<JobListResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{id:guid}", GetById)
            .WithName("GetJob")
            .Produces<JobResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    private static async Task<IResult> Submit(
        CreateJobRequest request,
        IServiceProvider services,
        TaskFlowInfrastructureStatus infrastructure,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!infrastructure.DatabaseConfigured)
        {
            return DatabaseNotConfigured();
        }

        if (!infrastructure.MessagingConfigured)
        {
            return MessagingNotConfigured();
        }

        JobSubmissionService submission = services.GetRequiredService<JobSubmissionService>();

        try
        {
            string? payloadJson = request.Payload.ValueKind == JsonValueKind.Undefined
                ? null
                : request.Payload.GetRawText();

            Job job = await submission.SubmitAsync(
                new SubmitJob(request.Type, payloadJson, request.MaxAttempts, request.CorrelationId),
                cancellationToken);

            loggerFactory.CreateLogger("TaskFlow.Api.Jobs").LogInformation(
                "Queued job. JobId={JobId} Type={JobType} Status={Status} CorrelationId={CorrelationId}",
                job.Id,
                job.Type,
                job.Status,
                job.CorrelationId);

            return Results.Created($"/api/v1/jobs/{job.Id}", JobResponseMapping.ToResponse(job));
        }
        catch (InvalidJobRequestException exception)
        {
            return Results.ValidationProblem(exception.Errors);
        }
        catch (JobNotQueuedException exception)
        {
            loggerFactory.CreateLogger("TaskFlow.Api.Jobs").LogWarning(
                "Stored job was not queued. JobId={JobId} Status={Status} ExceptionType={ExceptionType}",
                exception.JobId,
                JobStatus.Pending,
                exception.InnerException?.InnerException?.GetType().Name ?? exception.InnerException?.GetType().Name);
            return BrokerUnavailable(exception.JobId);
        }
        catch (JobQueuedStateNotSavedException exception)
        {
            LogDatabaseFailure(loggerFactory, exception);
            return Results.Problem(
                title: "SQL Server is unavailable.",
                detail: "The message was published, but the Queued status was not saved. The row still says Pending.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>
                {
                    ["jobId"] = exception.JobId,
                    ["jobStatus"] = nameof(JobStatus.Pending)
                });
        }
        catch (JobDatabaseUnavailableException exception)
        {
            LogDatabaseFailure(loggerFactory, exception);
            return DatabaseUnavailable();
        }
    }

    private static async Task<IResult> List(
        IServiceProvider services,
        ILoggerFactory loggerFactory,
        JobStatus? status,
        string? type,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        JobQueryService? queries = services.GetService<JobQueryService>();
        if (queries is null)
        {
            return DatabaseNotConfigured();
        }

        try
        {
            JobPage result = await queries.ListAsync(
                new JobListQuery(status, type, createdFrom, createdTo, page, pageSize),
                cancellationToken);

            JobListResponse body = new(
                result.Items.Select(JobResponseMapping.ToSummary).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount);

            return Results.Ok(body);
        }
        catch (InvalidJobRequestException exception)
        {
            return Results.ValidationProblem(exception.Errors);
        }
        catch (JobDatabaseUnavailableException exception)
        {
            LogDatabaseFailure(loggerFactory, exception);
            return DatabaseUnavailable();
        }
    }

    private static async Task<IResult> GetById(
        Guid id,
        IServiceProvider services,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        JobQueryService? queries = services.GetService<JobQueryService>();
        if (queries is null)
        {
            return DatabaseNotConfigured();
        }

        try
        {
            Job? job = await queries.GetAsync(id, cancellationToken);
            if (job is null)
            {
                return Results.Problem(title: "Job not found.", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(JobResponseMapping.ToResponse(job));
        }
        catch (JobDatabaseUnavailableException exception)
        {
            LogDatabaseFailure(loggerFactory, exception);
            return DatabaseUnavailable();
        }
    }

    private static IResult DatabaseNotConfigured()
    {
        return Results.Problem(
            title: "SQL Server is not configured.",
            detail: "Set ConnectionStrings:TaskFlow. Job routes need a database.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult MessagingNotConfigured()
    {
        return Results.Problem(
            title: "RabbitMQ is not configured.",
            detail: "Set ConnectionStrings:RabbitMq. The job was not stored.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult BrokerUnavailable(Guid jobId)
    {
        return Results.Problem(
            title: "RabbitMQ is unavailable.",
            detail: "The job was stored but not queued. It stays Pending and nothing republishes it yet.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?>
            {
                ["jobId"] = jobId,
                ["jobStatus"] = nameof(JobStatus.Pending)
            });
    }

    private static IResult DatabaseUnavailable()
    {
        return Results.Problem(
            title: "SQL Server is unavailable.",
            detail: "The job was not stored or could not be read. No message was published.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static void LogDatabaseFailure(ILoggerFactory loggerFactory, Exception exception)
    {
        loggerFactory.CreateLogger("TaskFlow.Api.Jobs").LogWarning(
            "SQL Server call failed. ExceptionType={ExceptionType}",
            exception.GetType().Name);
    }
}
