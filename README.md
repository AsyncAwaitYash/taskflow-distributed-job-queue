# TaskFlow

TaskFlow is a small, production-style **distributed background job queue**. A client will submit a job to an ASP.NET Core API. The API will store the job in SQL Server and hand the work to RabbitMQ. Workers will process jobs, acknowledge messages manually, retry failures with exponential backoff and jitter, and dead-letter jobs that keep failing.

**Phases 0–3 and the start of Phase 4 are in the repository.** Jobs are stored in SQL Server and published to RabbitMQ. A job is `Queued` only after the broker confirms. A worker process consumes the queue, runs the handler, and acks after the save. A retryable failure becomes `RetryScheduled` with a jittered backoff, or `DeadLettered` when the attempts run out. Nothing republishes that job yet.

## Status

| Area | State |
| --- | --- |
| Solution skeleton (.NET 10) | Built |
| `Job` / `JobAttempt` state machine | Unit-tested |
| SQL Server schema and job API | Create, list, and get |
| RabbitMQ topology and confirmed publisher | Built. New jobs are `Queued` |
| Worker with manual ack and job handlers | Built. Two workers compete for one queue |
| Retries, backoff, dead-lettering | Classification and backoff are built. The scheduler is not |
| Docker Compose | Not implemented |
| Serilog, health checks, OpenTelemetry | Not implemented |

Read [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md) before changing anything. The learning notes live in [docs/LEARNING_GUIDE.md](docs/LEARNING_GUIDE.md).

## Run it

Requires the .NET 10 SDK (`10.0.401` or a later `10.0` feature band). See [global.json](global.json). Integration tests that touch SQL Server or RabbitMQ also require Docker.

```bash
dotnet test TaskFlow.slnx
dotnet run --project src/TaskFlow.Api
```

The API listens on `http://localhost:8080`. Swagger is at `http://localhost:8080/swagger`. Without `ConnectionStrings:TaskFlow`, `GET /api/v1/jobs` returns 503. To store jobs, set `ConnectionStrings__TaskFlow` in the environment (`dotnet ef` reads it from there) and run:

```bash
dotnet tool restore
dotnet ef database update --project src/TaskFlow.Infrastructure --startup-project src/TaskFlow.Api
```

`dotnet ef` is the local tool in `dotnet-tools.json`. Do not put the SQL password in `appsettings.json`.

To submit jobs, also set `ConnectionStrings:RabbitMq` to an `amqp://` URI for a running broker (see [.env.example](.env.example)). Without it, `POST /api/v1/jobs` returns 503 and stores nothing. List and get still work.

To run the jobs, start the worker with the same two connection strings. It refuses to start without them. Start a second one with a different id to see competing consumers.

```bash
dotnet run --project src/TaskFlow.Worker --launch-profile worker-1
dotnet run --project src/TaskFlow.Worker --launch-profile worker-2
```

[docs/DEMO.md](docs/DEMO.md) is the full PowerShell walkthrough, from `docker run` to two workers sharing jobs, with the output it produced.

`scripts/verify.sh` builds the solution and runs the tests.

## Layout

```text
src/TaskFlow.Api              HTTP composition root
src/TaskFlow.Application      job submission, queries, JobProcessor, job handlers
src/TaskFlow.Domain           job model and state machine
src/TaskFlow.Infrastructure  EF Core, SQL Server, migrations, RabbitMQ publisher and consumer
src/TaskFlow.Worker           worker host (runs the RabbitMQ consumer)
tests/TaskFlow.UnitTests
tests/TaskFlow.IntegrationTests
```

Dependency direction: Api and Worker reference Application and Infrastructure. Infrastructure references Application and Domain. Application references Domain. Domain references nothing.

## What this project will not become

No React frontend, Redis, Kafka, Kubernetes, Elasticsearch, GraphQL, AI APIs, or authentication system. Swagger will be the UI once the API exists. Docker Compose arrives with the real services, not before.

## Continue the work

Say "Continue TaskFlow". The next session should read [AGENTS.md](AGENTS.md), [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md), and [docs/MASTER_PLAN.md](docs/MASTER_PLAN.md), then implement the next incomplete task only.
