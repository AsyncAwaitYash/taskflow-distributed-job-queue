# Current state

- Current phase: **Phase 2 complete**. Phase 3 has not started.
- Current task: none in progress.
- Last updated: 2026-10-03

## Completed

- Phase 0 solution skeleton and Phase 1 in-memory state machine
- EF Core mapping and migration `InitialJobSchema` for `Jobs` and `JobAttempts`
- `POST /api/v1/jobs`, `GET /api/v1/jobs`, `GET /api/v1/jobs/{id}`
- Swagger at `/swagger` and Problem Details for 400, 404, and 503
- New jobs stay `Pending`. RabbitMQ is not called
- Testcontainers SQL Server coverage for create, list, get, and attempt round-trip

## Pending

- Phase 3: RabbitMQ topology, publisher, then the consuming worker (next task is the publisher only)
- Phases 4–7: retry policy, idempotent claims, observability, Docker Compose, broader failure tests
- Phase 8 outbox and benchmark: not started, and not allowed yet

## Known issues

- A stored job is not queued. Restarting the API does not publish `Pending` rows.
- `RetryManually` still does not increase `MaxAttempts`. The HTTP retry route does not exist.
- The backoff delay is still an argument. No policy calculates it.
- `GET /` is a status probe, not `/health/live`.
- Assembly marker types remain for the layout test.
- RabbitMQ client version is not locked. On 2026-10-03 the newest stable `RabbitMQ.Client` on NuGet was 7.2.2. Re-check before adding the package.
- Docker Compose does not exist. Integration tests start their own SQL Server container.

## Recent changes

- Added the job HTTP API and the SQL Server migration.
- Kept created jobs at `Pending` so the response does not claim a publish happened.
- Proved the mapping with Testcontainers against SQL Server 2022.

## Next task

Add the RabbitMQ exchange, queue, and publisher. After a successful publish, call `Job.MarkQueued`. If the publish fails, leave the row `Pending` and return an error. Do not consume messages in that change.

## Build status

Succeeded on 2026-10-03 with SDK `10.0.401`.

`dotnet build TaskFlow.slnx`: 0 warnings, 0 errors.

## Test status

`dotnet test TaskFlow.slnx`: 55 passed, 0 failed (44 unit, 11 integration).

The SQL Server tests ran in Docker with `mcr.microsoft.com/mssql/server:2022-latest`. Without a connection string, `GET /api/v1/jobs` returns 503.

## Learning topics introduced

- A row is the system of record, and `Pending` is not `Queued`
- EF Core mapping of get-only domain properties
- Problem Details for invalid input and a missing database
- Testcontainers as a real SQL Server, not an in-memory substitute
- Job versus attempt, and the state machine, from Phase 1
