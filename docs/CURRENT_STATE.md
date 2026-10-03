# Current state

- Current phase: **Phase 3 in progress**. Tasks 1 (publisher), 2 (consumer), and 3 (handlers) are done.
- Current task: none in progress.
- Last updated: 2026-10-03

## Completed

- Phase 0 solution skeleton and Phase 1 in-memory state machine
- Phase 2: EF Core mapping, `POST/GET /api/v1/jobs`, Swagger, Problem Details
- Phase 3 task 1: topology `taskflow.jobs` / `taskflow.jobs.process` / `job.process`, confirmed publisher. A created job is `Queued` only after the broker confirms. A failed publish leaves `Pending` and returns 503 with `jobId` and `jobStatus`
- Phase 3 task 2: `TaskFlow.Worker` consumes `taskflow.jobs.process` with manual ack and prefetch 1 (`TaskFlow:Worker:PrefetchCount`). `JobProcessor` loads the job, claims it (`StartProcessing`, save), runs the handler, saves `Succeeded` or `Failed`, and only then is the message acked
- A delivery for a `Pending` row is promoted with `MarkQueued` and processed. A delivery for any other non-`Queued` status (terminal, `Processing`, `RetryScheduled`) is acked without running the handler
- An unreadable message is rejected without requeue. While SQL Server is unavailable, the worker waits `DatabaseRetryDelay` (5 s) and then nacks with requeue
- Phase 3 task 3: `IJobHandler` and `JobHandlerRegistry`. Handlers: `demo.success`, `demo.permanent-failure`, `demo.slow` (0–30 s), `email.send` (simulated, needs `to`), `report.generate`, `data.process`. The API validates `type` against the same registry
- Migration `JobClientGeneratedIds` marks `Jobs.Id` and `JobAttempts.Id` as client-generated so a new attempt on a tracked job is inserted, not updated. No schema change

## Pending

- Phase 3: competing consumers (two worker processes) and the end-to-end happy path (next)
- Phases 4–7: retry policy, idempotent claims, observability, Docker Compose, broader failure tests
- Phase 8 outbox and benchmark: not started, and not allowed yet

## Known issues

- `demo.transient-failure` is rejected with 400. It needs the Phase 4 retry policy.
- Any exception from a handler is a permanent failure (`ErrorType` = exception type name). Phase 4 classifies failures.
- A worker crash after the claim save leaves the job `Processing`. The redelivery is skipped, so the job is stuck. Phase 5 handles stuck claims.
- The claim is a plain save, not a compare-and-update. Two workers that load the same `Queued` row at once could both run it. RabbitMQ gives each delivery to one consumer, so this needs a redelivery overlap. Phase 5 adds the conditional claim.
- If SQL Server fails after the claim save, the message is nacked and the redelivery sees `Processing` and is skipped, which is the same stuck case.
- An unexpected processing error (not a SQL outage) rejects the message without requeue. The row keeps whatever was last saved.
- A `Pending` row left by a failed publish is never republished.
- The API opens one channel per publish. No channel pool.
- `RetryManually` still does not increase `MaxAttempts`. The HTTP retry route does not exist.
- `GET /` is a status probe, not `/health/live`. Logs are the default console logger, not Serilog.
- Assembly marker types remain for the layout test.
- Docker Compose does not exist. Integration tests start their own SQL Server and RabbitMQ containers.
- This machine has a second, authenticated NuGet source. Restore with `dotnet restore TaskFlow.slnx --source https://api.nuget.org/v3/index.json`. The repository has no `nuget.config`.

## Recent changes

- Replaced the worker skeleton with `RabbitMqJobConsumer`. The worker refuses to start without both connection strings.
- Replaced `KnownJobTypes` with `JobHandlerRegistry`.
- Added migration `JobClientGeneratedIds` (snapshot only).

## Next task

Run two `TaskFlow.Worker` processes against one queue and prove competing consumers: several jobs submitted, each ending with one attempt recorded by one of the two workers, with the `WorkerId` on that attempt. This is a happy-path test, not an exactly-once claim. Add the end-to-end happy-path test and the demo steps. Do not add the compare-and-update claim (Phase 5) or Docker Compose (Phase 6).

## Build status

Succeeded on 2026-10-03 with SDK `10.0.401`.

`dotnet build TaskFlow.slnx`: 0 warnings, 0 errors.

## Test status

`dotnet test TaskFlow.slnx`: 88 passed, 0 failed (68 unit, 20 integration).

The integration tests ran in Docker with `mcr.microsoft.com/mssql/server:2022-latest` and `rabbitmq:4.1`. `WorkerTests` runs the real consumer in-process.

## Learning topics introduced

- Manual ack after the outcome is saved, and what each settle call (`ack`, `nack`, `reject`) means
- Prefetch as the limit on unacknowledged messages per consumer
- Skipping a redelivery by reading the row, not the message
- A handler abstraction that knows nothing about RabbitMQ or SQL Server
- The EF pitfall with client-generated keys on a tracked aggregate
