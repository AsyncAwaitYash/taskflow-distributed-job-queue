# Master plan

Phases are sequential. Phase 8 waits until Phases 0–7 are stable. One task per change. "Continue TaskFlow" means the next incomplete task, not the rest of the phase.

## Phase 0 — Foundation and project memory

Status: **done** (2026-10-03).

- Repository structure, `AGENTS.md`, `.cursor/rules`
- Design docs listed in the master prompt
- Buildable projects: Api, Application, Domain, Infrastructure, Worker, unit tests, integration tests
- Build and test verification

Not in this phase: job submission, RabbitMQ, worker processing, retries, idempotency, handlers.

## Phase 1 — Architecture and domain

Status: **done** (2026-10-03).

- `Job` and `JobAttempt` in `src/TaskFlow.Domain/Jobs`
- `JobTransitions` is the only legal status graph, covered by unit tests
- No EF Core and no HTTP endpoints in this phase

## Phase 2 — API and SQL Server

Status: **done** (2026-10-03).

- EF Core maps `Job` and `JobAttempt`. Migration `InitialJobSchema` creates `Jobs` and `JobAttempts`.
- `POST /api/v1/jobs`, `GET /api/v1/jobs`, and `GET /api/v1/jobs/{id}`
- New jobs stay `Pending`. Nothing is published to RabbitMQ.
- Swagger, Problem Details, pagination, and filters for status, type, and created time

## Phase 3 — RabbitMQ and worker

Status: **done** (2026-10-03).

- Task 1, **done** (2026-10-03): exchange `taskflow.jobs`, queue `taskflow.jobs.process`, routing key `job.process`, and the confirmed publisher. `Job.MarkQueued` runs only after the broker confirms. A failed publish leaves the row `Pending` and returns 503. `RabbitMQ.Client` 7.2.2 is pinned.
- Task 2, **done** (2026-10-03): worker consumer with manual ack and prefetch 1. Ack after the outcome is saved. `Pending` deliveries are promoted, other non-`Queued` deliveries are skipped.
- Task 3, **done** (2026-10-03, same change as task 2): `IJobHandler`, `JobHandlerRegistry`, and the handlers that succeed or fail permanently. `demo.transient-failure` waits for Phase 4.
- Task 4, **done** (2026-10-03): competing consumers proven with two worker hosts in `CompetingConsumersTests`, end-to-end happy path across every handler, and a two-process manual run recorded in `docs/DEMO.md`. Fixed along the way: a reader/writer deadlock (migration `EnableReadCommittedSnapshot`) and `dotnet ef database update` ignoring `ConnectionStrings__TaskFlow`.

## Phase 4 — Reliability

Status: **next**.

- Task 1, **next**: failure classification (retryable versus permanent, including unexpected exceptions) and the exponential backoff with jitter policy, configured rather than hard-coded. The worker records `RetryScheduled` with `NextAttemptAt` through `Job.RecordRetryableFailure`, and `DeadLettered` when attempts run out. Add the `demo.transient-failure` handler.
- Task 2: the `NextAttemptAt` retry scheduler and its filtered `(Status, NextAttemptAt)` index. It conditionally moves due jobs to `Queued` and publishes them.
- Task 3: `POST /api/v1/jobs/{id}/retry` for `Failed` and `DeadLettered` jobs, `409` when the status cannot transition.

## Phase 5 — Idempotency and concurrency

- Terminal jobs are acknowledged without running again
- Compare-and-update claims
- Tests: duplicate message, two workers, one job, simultaneous retry scheduling
- Document at-least-once and the crash-before-ack case

## Phase 6 — Observability and developer experience

- Serilog with JobId, AttemptNumber, WorkerId, CorrelationId, Status, Duration
- Correlation ids
- `/health/live` and `/health/ready`
- OpenTelemetry only after the core path works
- Docker Compose: SQL Server, RabbitMQ, API, worker-1, worker-2
- Demo handlers wired into the compose demo

## Phase 7 — Testing and polish

- Failure tests: RabbitMQ down, SQL Server down, permanent failure, max attempts
- README, architecture diagram, `DEMO.md` turned into a script that actually runs

## Phase 8 — Optional

- Transactional outbox
- A real benchmark of 1 vs 2 vs 4 workers
- Only measured numbers get written down
