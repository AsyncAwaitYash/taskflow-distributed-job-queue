# Testing

## What runs today

| Project | What it proves |
| --- | --- |
| `tests/TaskFlow.UnitTests` | Domain has no TaskFlow dependencies. Application references Domain only. Infrastructure references Application and Domain only. `Job` accepts only the legal status graph. `JobSubmissionService` stores before it publishes, marks `Queued` only after a confirmed publish, leaves the job `Pending` when the publish fails, reports a failed `Queued` save separately, and never publishes a rejected request. The publisher here is a recording fake in the test project. `JobProcessorTests`: a `Queued` job is claimed, run, and saved; a `Pending` row is promoted; terminal and `Processing` rows are skipped without running the handler; a missing handler and a thrown exception become `Failed` without storing the exception text; a SQL failure on the claim stops before the handler. Handler tests cover `demo.slow` and `email.send` payload checks, and the registry rejects duplicates and excludes `demo.transient-failure`. |
| `tests/TaskFlow.IntegrationTests` | Without connection strings, `GET /` reports phase 3 and job routes return 503. Swagger lists the job paths. With Testcontainers SQL Server and RabbitMQ: create/list/get persist a `Queued` job; the message is persistent, routed through `taskflow.jobs` / `job.process`, and carries only `jobId`, `type`, and `correlationId`; the first publish declares the exchange and queue; an unreachable broker returns 503 with the `jobId` and the row stays `Pending`; a missing RabbitMQ setting returns 503 and stores nothing. `WorkerTests` starts the real consumer in-process (same extension calls as `TaskFlow.Worker`): `demo.success` reaches `Succeeded` with one attempt by `test-worker` and the queue drains; `demo.permanent-failure` reaches `Failed`; a duplicate message for a `Succeeded` job adds no attempt; a message for a `Pending` row runs the job; an unreadable message is rejected and does not block the queue. |

Command:

```bash
dotnet test TaskFlow.slnx
```

`scripts/verify.sh` builds, then tests.

On 2026-10-03 with SDK 10.0.401: build 0 warnings, 88 tests passed (68 unit, 20 integration).

The integration tests use Testcontainers and require Docker: `mcr.microsoft.com/mssql/server:2022-latest` and `rabbitmq:4.1`. All tests in `InfrastructureCollection` share one SQL Server and one RabbitMQ container and run one after another, which is why the message test can purge the queue first. The database is not reset between tests, so count assertions compare against a count taken at the start of the test. A `WorkerTests` consumer also processes messages left by earlier tests; that is harmless because those jobs are real. Production code has no fake broker.

The "unreachable broker" test points the API at `amqp://127.0.0.1:1` with a 3-second publish timeout. It does not stop the shared container.

## Not covered yet

- The broker nacking a publish, or returning it as unroutable. Both map to `JobPublishFailedException` in `RabbitMqJobPublisher`, but no test forces them.
- SQL Server failing after a confirmed publish against real infrastructure. Only the unit test covers that branch.
- The worker's delayed nack while SQL Server is down. The consumer handles it, but no test stops SQL Server.
- A worker process killed mid-job.

## What later phases add

Unit:

- Retry classification
- Backoff and jitter bounds

Integration:

- Two worker processes on one queue (Phase 3 task 4)
- Transient failure and max attempts
- A real crash before ack
- Two workers racing the same job id
- Two scheduler passes racing the same retry

Failure:

- RabbitMQ stopped mid-run, not only unreachable from the start
- SQL Server down during claim

Prefer those behaviors over a coverage percentage.

## How the API test hosts the app

`public partial class Program` in `src/TaskFlow.Api/Program.cs` exposes the top-level entry point to `WebApplicationFactory<Program>`. The factory uses the in-memory test server. It does not bind port 8080. Each test sets `ConnectionStrings:TaskFlow` and `ConnectionStrings:RabbitMq` with `UseSetting`.

## Restoring packages on this machine

This machine has a second, authenticated NuGet source configured globally. With central package management, NuGet treats the two sources as an error (NU1507) and the private feed returns 401. Restore with `dotnet restore TaskFlow.slnx --source https://api.nuget.org/v3/index.json`, then build or test as usual.
