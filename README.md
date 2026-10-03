# TaskFlow

TaskFlow is a small, production-style **distributed background job queue**. A client will submit a job to an ASP.NET Core API. The API will store the job in SQL Server and hand the work to RabbitMQ. Workers will process jobs, acknowledge messages manually, retry failures with exponential backoff and jitter, and dead-letter jobs that keep failing.

**Phases 0–2 are in the repository.** Jobs can be stored in SQL Server. They stay `Pending` because RabbitMQ is not connected. Workers do not consume a queue yet.

## Status

| Area | State |
| --- | --- |
| Solution skeleton (.NET 10) | Built |
| `Job` / `JobAttempt` state machine | In memory, unit-tested |
| SQL Server schema and job API | Create, list, and get. New jobs stay `Pending` |
| RabbitMQ, workers that consume, Docker Compose | Not implemented |
| Serilog, health checks, OpenTelemetry | Not implemented |

Read [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md) before changing anything. The learning notes live in [docs/LEARNING_GUIDE.md](docs/LEARNING_GUIDE.md).

## Run it

Requires the .NET 10 SDK (`10.0.401` or a later `10.0` feature band). See [global.json](global.json). Integration tests that touch SQL Server also require Docker.

```bash
dotnet test TaskFlow.slnx
dotnet run --project src/TaskFlow.Api
```

The API listens on `http://localhost:8080`. Swagger is at `http://localhost:8080/swagger`. Without `ConnectionStrings:TaskFlow`, `GET /api/v1/jobs` returns 503. To store jobs, point that connection string at SQL Server and run:

```bash
dotnet tool restore
dotnet ef database update --project src/TaskFlow.Infrastructure --startup-project src/TaskFlow.Api
```

`dotnet ef` is the local tool in `dotnet-tools.json`. Do not put the SQL password in `appsettings.json`.

`scripts/verify.sh` builds the solution and runs the tests.

## Layout

```text
src/TaskFlow.Api              HTTP composition root
src/TaskFlow.Application      job submission and queries
src/TaskFlow.Domain           job model and state machine
src/TaskFlow.Infrastructure  EF Core, SQL Server, migrations
src/TaskFlow.Worker           worker host (stays alive; consumes nothing)
tests/TaskFlow.UnitTests
tests/TaskFlow.IntegrationTests
```

Dependency direction: Api and Worker reference Application and Infrastructure. Infrastructure references Application and Domain. Application references Domain. Domain references nothing.

## What this project will not become

No React frontend, Redis, Kafka, Kubernetes, Elasticsearch, GraphQL, AI APIs, or authentication system. Swagger will be the UI once the API exists. Docker Compose arrives with the real services, not before.

## Continue the work

Say "Continue TaskFlow". The next session should read [AGENTS.md](AGENTS.md), [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md), and [docs/MASTER_PLAN.md](docs/MASTER_PLAN.md), then implement the next incomplete task only.
