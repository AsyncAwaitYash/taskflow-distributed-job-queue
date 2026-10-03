# Current state

- Current phase: **Phase 4 in progress**. Task 1 is done. The retry scheduler is not built.
- Current task: none in progress.
- Last updated: 2026-10-03

## Completed

- Phase 0 solution skeleton and Phase 1 in-memory state machine
- Phase 2: EF Core mapping, `POST/GET /api/v1/jobs`, Swagger, Problem Details
- Phase 3 task 1: topology `taskflow.jobs` / `taskflow.jobs.process` / `job.process`, confirmed publisher. A created job is `Queued` only after the broker confirms. A failed publish leaves `Pending` and returns 503 with `jobId` and `jobStatus`
- Phase 3 task 2: `TaskFlow.Worker` consumes with manual ack and prefetch 1. `JobProcessor` claims, runs the handler, saves `Succeeded` or `Failed`, and the message is acked after that save. `Pending` deliveries are promoted; other non-`Queued` deliveries are acked without running
- Phase 3 task 3: `IJobHandler`, `JobHandlerRegistry`, and handlers `demo.success`, `demo.permanent-failure`, `demo.slow`, `email.send` (simulated), `report.generate`, `data.process`
- Phase 3 task 4: two workers compete for one queue. `CompetingConsumersTests` runs `worker-a` and `worker-b` hosts: 8 one-second jobs end with one attempt each, and both worker ids appear. An end-to-end test covers every handler. `docs/DEMO.md` is a script that was run on this machine with two real worker processes
- Migration `EnableReadCommittedSnapshot`: reads use row versions, which removed a deadlock (SQL error 1205) between `GET /api/v1/jobs/{id}` and a worker writing the same job
- `dotnet ef database update` now reads `ConnectionStrings__TaskFlow` from the environment
- Worker launch profiles `worker-1` and `worker-2`
- Phase 4 task 1: `JobFailureClassifier`, `RetryBackoffPolicy` (`TaskFlow:Retry`, defaults about 5s, 25s, 125s, then 10 minutes, jitter 0.2), and `demo.transient-failure`. A retryable failure is saved as `RetryScheduled` with `NextAttemptAt`, or `DeadLettered` when `AttemptCount` reaches `MaxAttempts`, and the message is acked. Unknown exceptions are retryable. `JsonException`, `ArgumentException`, `FormatException`, and `NotSupportedException` are permanent

## Pending

- Phase 4: the retry scheduler (next), then the manual retry endpoint
- Phases 5–7: conditional claims and crash recovery, observability and Docker Compose, broader failure tests
- Phase 8 outbox and benchmark: not started, and not allowed yet

## Known issues

- A `RetryScheduled` job is never republished. The scheduler is the next task, so `demo.transient-failure` stops after its first failure even when later attempts would succeed.
- A worker crash after the claim save leaves the job `Processing`. The redelivery is skipped, so the job is stuck. Phase 5.
- The claim is a plain save, not a compare-and-update. Two workers holding deliveries for the same `Queued` row at once could both run it. `CompetingConsumersTests` does not exercise that case, because each message goes to one consumer. Phase 5.
- If SQL Server fails after the claim save, the message is nacked and the redelivery sees `Processing` and is skipped. Same stuck case.
- An unexpected processing error (not a SQL outage) rejects the message without requeue.
- A `Pending` row left by a failed publish is never republished.
- The API opens one channel per publish. No channel pool.
- `RetryManually` still does not increase `MaxAttempts`. The HTTP retry route does not exist.
- `GET /` is a status probe, not `/health/live`. Logs are the default console logger, not Serilog.
- The tests read `ConnectionStrings__*` from the environment like the app does. Running them in a terminal set up for the demo makes the "not configured" tests fail. Clear the variables first.
- Assembly marker types remain for the layout test.
- Docker Compose does not exist. The demo uses `docker run`; the integration tests start their own containers.
- This machine has a second, authenticated NuGet source. Restore with `dotnet restore TaskFlow.slnx --source https://api.nuget.org/v3/index.json`. The repository has no `nuget.config`.

## Recent changes

- Added failure classification and exponential backoff with jitter. `demo.transient-failure` is a real handler.
- The worker now saves `RetryScheduled` or `DeadLettered` and still acks. It does not requeue a failed job.

## Next task

Phase 4 task 2: the retry scheduler. Query `RetryScheduled` rows whose `NextAttemptAt` is due, through a filtered index on `Status` and `NextAttemptAt`. Conditionally move each row to `Queued` and publish it. A second pass that updates zero rows does not publish. Do not build the manual retry endpoint in that change.

## Build status

Succeeded on 2026-10-03 with SDK `10.0.401`.

`dotnet build TaskFlow.slnx`: 0 warnings, 0 errors.

## Test status

`dotnet test TaskFlow.slnx`: 123 passed, 0 failed (98 unit, 25 integration), run twice.

The integration tests ran in Docker with `mcr.microsoft.com/mssql/server:2022-latest` and `rabbitmq:4.1`.

## Learning topics introduced

- Retryable versus permanent failures, and why an unknown exception is retryable
- Exponential backoff with jitter, and why the message is acked instead of requeued
- Dead-lettering as a job status, with the attempt still recorded as a retryable failure
- Competing consumers: one message goes to one consumer, and prefetch 1 sends the next one to an idle worker
- What a two-worker test proves and what it does not (no conditional claim, no crash)
- Reader/writer deadlocks in SQL Server and `READ_COMMITTED_SNAPSHOT`
- A flaky test as evidence of a production bug, not noise
