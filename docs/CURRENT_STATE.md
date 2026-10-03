# Current state

- Current phase: **Phase 1 complete**. Phase 2 has not started.
- Current task: none in progress.
- Last updated: 2026-10-03

## Completed

- Phase 0 solution skeleton, docs, and cursor rules
- `Job`, `JobAttempt`, `JobStatus`, and `JobTransitions` in `src/TaskFlow.Domain/Jobs`
- Unit tests for the legal graph: success, retry scheduling, permanent failure, exhausted budget, manual retry, and rejected transitions
- `GET /` still reports that job processing is off. The worker still does not consume a queue

## Pending

- Phase 2: EF Core, SQL Server, and the job HTTP API (next)
- Phases 3–7: RabbitMQ, retry policy, idempotent claims, observability, Docker, broader tests
- Phase 8 outbox and benchmark: not started, and not allowed yet

## Known issues

- The state machine is in memory only. A process restart drops every job, because nothing persists it.
- `GET /` is a temporary probe. It is not `/health/live` and not the job API.
- Assembly marker types remain so the layout test can see project references. They are not the domain model.
- `RetryManually` does not increase `MaxAttempts`. A job that already used its budget can be queued again, and the next retryable failure dead-letters it immediately. Phase 4 can change that when the retry endpoint exists.
- The backoff delay is an argument to `RecordRetryableFailure`. No policy calculates it yet.
- RabbitMQ client version is not locked. On 2026-10-03 the newest stable `RabbitMQ.Client` on NuGet was 7.2.2. Re-check before Phase 3.
- Docker Compose does not exist. `.env.example` is documentation only.

## Recent changes

- Added the job aggregate and the attempt entity.
- Locked the status graph with unit tests.
- Pointed the design docs at the types that now exist.

## Next task

Persist `Job` with EF Core and add `POST /api/v1/jobs`, `GET /api/v1/jobs`, and `GET /api/v1/jobs/{id}`. Do not publish to RabbitMQ in that change. Status changes still go through `Job`.

## Build status

Succeeded on 2026-10-03 with SDK `10.0.401`.

`dotnet build TaskFlow.slnx`: 0 warnings, 0 errors.

## Test status

`dotnet test TaskFlow.slnx`: 37 passed, 0 failed (35 unit, 2 integration).

The Phase 0 smoke run is unchanged: `GET /` returns `jobProcessing: false`, and the job routes return 404.

## Learning topics introduced

- Job versus attempt
- A closed set of status transitions, including why `Succeeded` has no exit
- Retry scheduling versus dead-lettering when the attempt budget is spent
- Manual retry as a separate entry point from publishing
- Layering and the repository-as-source-of-truth lesson from Phase 0
