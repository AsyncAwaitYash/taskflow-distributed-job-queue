# Current state

- Current phase: **Phase 0 complete**. Phase 1 has not started.
- Current task: none in progress.
- Last updated: 2026-10-03

## Completed

- .NET 10 solution (`TaskFlow.slnx`) with Api, Application, Domain, Infrastructure, Worker, unit tests, and integration tests
- Project references match the dependency rules. Unit tests lock that graph
- `GET /` returns a phase 0 skeleton document. Job routes are absent
- Worker process starts, logs that job processing is disabled, and stops cleanly on cancellation
- Project memory: `AGENTS.md`, `.cursor/rules`, and the `docs/` set
- Central package versions in `Directory.Packages.props`. No RabbitMQ, EF Core, or Serilog packages yet

## Pending

- Phase 1 domain model and state machine (next)
- Phases 2–7: API, SQL Server, RabbitMQ, retries, idempotency, observability, Docker, broader tests
- Phase 8 outbox and benchmark: not started, and not allowed yet

## Known issues

- `GET /` is a temporary probe so the skeleton can be run. It is not `/health/live` and not the job API. Remove or replace it when real endpoints exist.
- Assembly marker types (`DomainAssembly`, `ApplicationAssembly`, `InfrastructureAssembly`) exist so the empty layers still compile and the reference graph is testable. Delete them when real types make the references obvious.
- The design docs describe target behavior. None of that behavior runs yet. Do not demo it as if it does.
- RabbitMQ client version is not locked. On 2026-10-03 the newest stable `RabbitMQ.Client` on NuGet was 7.2.2. Re-check before Phase 3.
- Docker Compose does not exist. `.env.example` is documentation only; the apps do not read it.

## Recent changes

- Initialized the Phase 0 skeleton and wrote the design and learning docs.
- Verified the build, the test suite, a live `GET /`, and a short worker run.

## Next task

Implement the `Job` and `JobAttempt` domain model and the legal status transitions, with unit tests. Do not add EF Core, migrations, or HTTP endpoints in that change.

## Build status

Succeeded on 2026-10-03 with SDK `10.0.401`.

`dotnet build TaskFlow.slnx`: 0 warnings, 0 errors.

## Test status

`dotnet test TaskFlow.slnx`: 5 passed, 0 failed (3 unit, 2 integration).

Smoke run:

- `GET http://127.0.0.1:8080/` returned the skeleton JSON (`jobProcessing: false`)
- `GET /api/v1/jobs` returned 404
- `GET /health/live` returned 404
- The worker logged `JobProcessingEnabled=False` and logged a clean stop when cancelled

## Learning topics introduced

- What TaskFlow is for (planned product, skeleton only)
- Asynchronous processing, queues, and RabbitMQ at a high level, labeled planned
- Layered projects and why the dependency direction exists (implemented)
- The repository, not chat history, holds the project memory (implemented)
