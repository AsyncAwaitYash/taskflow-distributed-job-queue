# TaskFlow

TaskFlow is a small, production-style **distributed background job queue**. A client will submit a job to an ASP.NET Core API. The API will store the job in SQL Server and hand the work to RabbitMQ. Workers will process jobs, acknowledge messages manually, retry failures with exponential backoff and jitter, and dead-letter jobs that keep failing.

**This repository is Phase 0 only.** The solution, project layout, tests, and design docs exist. Job submission, SQL Server, RabbitMQ, retries, and handlers do not.

## Status

| Area | State |
| --- | --- |
| Solution skeleton (.NET 10) | Built |
| `GET /` skeleton probe | Runs |
| `POST /api/v1/jobs` and the rest of the job API | Not implemented |
| SQL Server, EF Core, RabbitMQ, Docker Compose | Not implemented |
| Serilog, health checks, OpenTelemetry | Not implemented |

Read [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md) before changing anything. The learning notes live in [docs/LEARNING_GUIDE.md](docs/LEARNING_GUIDE.md).

## Run the skeleton

Requires the .NET 10 SDK (`10.0.401` or a later `10.0` feature band). See [global.json](global.json).

```bash
dotnet test TaskFlow.slnx
dotnet run --project src/TaskFlow.Api
dotnet run --project src/TaskFlow.Worker
```

The API listens on `http://localhost:8080`. `GET /` returns JSON that says this is a phase 0 skeleton and job processing is off. `GET /api/v1/jobs` is not implemented and returns 404.

`scripts/verify.sh` builds the solution and runs the tests.

## Layout

```text
src/TaskFlow.Api              HTTP composition root
src/TaskFlow.Application      use cases (empty until later phases)
src/TaskFlow.Domain           job model (empty until Phase 1)
src/TaskFlow.Infrastructure  SQL Server and RabbitMQ (empty until later phases)
src/TaskFlow.Worker           worker host (stays alive; consumes nothing)
tests/TaskFlow.UnitTests
tests/TaskFlow.IntegrationTests
```

Dependency direction: Api and Worker reference Application and Infrastructure. Infrastructure references Application and Domain. Application references Domain. Domain references nothing.

## What this project will not become

No React frontend, Redis, Kafka, Kubernetes, Elasticsearch, GraphQL, AI APIs, or authentication system. Swagger will be the UI once the API exists. Docker Compose arrives with the real services, not before.

## Continue the work

Say "Continue TaskFlow". The next session should read [AGENTS.md](AGENTS.md), [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md), and [docs/MASTER_PLAN.md](docs/MASTER_PLAN.md), then implement the next incomplete task only.
