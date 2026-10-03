# Changelog

## Unreleased

### Added

- `TaskFlow.Worker` consumes `taskflow.jobs.process` with manual ack and prefetch 1. Jobs reach `Succeeded` or `Failed`, with one attempt row per run and the worker id on it.
- Job handlers: `demo.success`, `demo.permanent-failure`, `demo.slow`, `email.send` (simulated), `report.generate`, `data.process`.
- `TaskFlow:Worker` settings: `PrefetchCount`, `WorkerId`, `DatabaseRetryDelay`, `ConnectRetryDelay`.
- Migration `JobClientGeneratedIds` (model snapshot only, no schema change).

### Changed (worker)

- `demo.transient-failure` is rejected with 400 until the Phase 4 retry policy exists.
- The worker refuses to start without `ConnectionStrings:TaskFlow` and `ConnectionStrings:RabbitMq`.

### Added (publisher)

- RabbitMQ topology (`taskflow.jobs`, `taskflow.jobs.process`, `job.process`) declared by the API on first connection.
- `POST /api/v1/jobs` publishes `{ jobId, type, correlationId }` with publisher confirms and returns `Queued` only after the broker confirms.
- 503 Problem Details with `jobId` and `jobStatus: "Pending"` when the row was stored but the publish failed, or when the publish succeeded and the `Queued` status was not saved.
- `ConnectionStrings:RabbitMq` and `TaskFlow:RabbitMq:PublishTimeout` settings. `GET /` reports `messagingConfigured`.

### Changed

- `POST /api/v1/jobs` returns 503 without storing anything when RabbitMQ is not configured. Created jobs are `Queued`, not `Pending`.

### Added (earlier)

- SQL Server persistence for `Job` and `JobAttempt`, migration `InitialJobSchema`.
- `POST /api/v1/jobs`, `GET /api/v1/jobs`, and `GET /api/v1/jobs/{id}`. Created jobs stay `Pending`.
- Swagger UI and Problem Details. Job routes return 503 when SQL Server is not configured.
- In-memory `Job` and `JobAttempt` model. Status changes only through `Job`, and illegal pairs throw `InvalidJobTransitionException`.
- Unit tests for the happy path, retry scheduling, permanent failure, exhausted attempts, manual retry, and rejected transitions.
- Phase 0 solution skeleton for .NET 10: API, application, domain, infrastructure, worker, unit tests, and integration tests.
- `GET /` skeleton response. Job processing is explicitly disabled.
- Worker host that starts and stops without consuming a queue.
- Project-memory docs, ADRs, cursor rules, and `AGENTS.md`.
- `scripts/verify.sh` to build and test.
- `.env.example` placeholders for the SQL Server and RabbitMQ settings later phases will read.

### Not in this version

Retries and backoff, the retry endpoint, compare-and-update claims, recovery of jobs stuck in `Processing`, republishing `Pending` rows, health checks, and Docker Compose.
