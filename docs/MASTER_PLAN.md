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

Status: **next**.

- ASP.NET Core job endpoints
- EF Core, the first migration, SQL Server
- Create, list, and get a job
- Swagger and Problem Details
- Pagination and filters (`status`, `type`, date range) after the basic flow works

## Phase 3 — RabbitMQ and worker

- Exchange `taskflow.jobs`, queue `taskflow.jobs.process`, routing key `job.process`
- Publisher, worker consumer, manual ack
- Handler abstraction and the demo handlers
- Competing consumers (two worker processes)
- End-to-end happy path

Lock `RabbitMQ.Client` only in this phase, after re-checking the current NuGet release and the official .NET client guide.

## Phase 4 — Reliability

- Failure classification
- Exponential backoff with jitter, configured rather than hard-coded
- `NextAttemptAt` retry scheduler and its index
- Dead-letter state
- `POST /api/v1/jobs/{id}/retry`

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
