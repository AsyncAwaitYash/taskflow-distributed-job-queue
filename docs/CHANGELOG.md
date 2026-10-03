# Changelog

## Unreleased

### Added

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

Job submission, SQL Server, RabbitMQ, the backoff calculator, handlers, health checks, and Docker Compose. The domain can schedule a retry time, but nothing computes the delay or publishes a message.
