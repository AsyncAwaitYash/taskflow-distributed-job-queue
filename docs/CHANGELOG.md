# Changelog

## Unreleased

### Added

- Phase 0 solution skeleton for .NET 10: API, application, domain, infrastructure, worker, unit tests, and integration tests.
- `GET /` skeleton response. Job processing is explicitly disabled.
- Worker host that starts and stops without consuming a queue.
- Project-memory docs, ADRs, cursor rules, and `AGENTS.md`.
- `scripts/verify.sh` to build and test.
- `.env.example` placeholders for the SQL Server and RabbitMQ settings later phases will read.

### Not in this version

Job submission, persistence, RabbitMQ, retries, handlers, health checks, and Docker Compose.
