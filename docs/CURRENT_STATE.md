# Current state

- Current phase: **Phase 3 complete**. Phase 4 has not started.
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

## Pending

- Phase 4: failure classification and backoff (next), the retry scheduler, the manual retry endpoint
- Phases 5–7: conditional claims and crash recovery, observability and Docker Compose, broader failure tests
- Phase 8 outbox and benchmark: not started, and not allowed yet

## Known issues

- `demo.transient-failure` is rejected with 400. It needs the Phase 4 retry policy.
- Any exception from a handler is a permanent failure (`ErrorType` = exception type name). Phase 4 classifies failures.
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

- Added `CompetingConsumersTests` and the shared test helpers `TestWorkerHost` and `TestJobApi`.
- The integration tests now use a `TaskFlow` database inside the SQL Server container. Before, they wrote the tables into `master`.
- Added migration `EnableReadCommittedSnapshot` and a test that it is on.
- Fixed the design-time `DbContext` factory to honor `ConnectionStrings__TaskFlow`.
- Rewrote `docs/DEMO.md` from an observed run.

## Next task

Phase 4 task 1: classify failures as retryable or permanent, add a configured exponential backoff with jitter policy, and have the worker record `RetryScheduled` with `NextAttemptAt` through `Job.RecordRetryableFailure`, or `DeadLettered` when attempts run out. Add the `demo.transient-failure` handler. Ack after the save; never requeue a failed job. The scheduler that republishes due jobs is Phase 4 task 2, so a `RetryScheduled` job will wait until that task lands.

## Build status

Succeeded on 2026-10-03 with SDK `10.0.401`.

`dotnet build TaskFlow.slnx`: 0 warnings, 0 errors.

## Test status

`dotnet test TaskFlow.slnx`: 91 passed, 0 failed (68 unit, 23 integration). The integration suite passed 5 runs in a row after the snapshot-isolation fix; before it, 2 of 4 runs failed with a deadlock.

The integration tests ran in Docker with `mcr.microsoft.com/mssql/server:2022-latest` and `rabbitmq:4.1`.

## Learning topics introduced

- Competing consumers: one message goes to one consumer, and prefetch 1 sends the next one to an idle worker
- What a two-worker test proves and what it does not (no conditional claim, no crash)
- Reader/writer deadlocks in SQL Server and `READ_COMMITTED_SNAPSHOT`
- A flaky test as evidence of a production bug, not noise
