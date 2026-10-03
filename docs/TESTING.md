# Testing

## What runs today

| Project | What it proves |
| --- | --- |
| `tests/TaskFlow.UnitTests` | Domain has no TaskFlow dependencies. Application references Domain only. Infrastructure references Application and Domain only. `Job` accepts only the legal status graph: success, retry scheduling, permanent failure, exhausted attempts, manual retry, and rejected or out-of-order calls leave the job unchanged. |
| `tests/TaskFlow.IntegrationTests` | The API host starts. `GET /` returns the phase 0 skeleton. The API assembly references Application and Infrastructure. |

Command:

```bash
dotnet test TaskFlow.slnx
```

`scripts/verify.sh` builds, then tests.

On 2026-10-03 with SDK 10.0.401: build 0 warnings, 37 tests passed (35 unit, 2 integration).

There are no Testcontainers yet. Nothing in the suite talks to SQL Server or RabbitMQ, because those integrations do not exist. Do not add a fake broker so a test can go green.

## What later phases add

Unit:

- Legal and illegal job transitions
- Retry classification
- Backoff and jitter bounds
- Handler resolution
- Idempotent "already terminal" decision

Integration:

- API persists a job in SQL Server
- API publishes, worker consumes, job reaches `Succeeded`
- Transient failure, permanent failure, max attempts
- Duplicate delivery and crash-before-ack
- Two workers racing the same job id
- Two scheduler passes racing the same retry

Failure:

- Broker down during publish
- SQL Server down during submit and during claim

Prefer those behaviors over a coverage percentage.

## How the API test hosts the app

`public partial class Program` in `src/TaskFlow.Api/Program.cs` exposes the top-level entry point to `WebApplicationFactory<Program>`. The factory uses the in-memory test server. It does not bind port 8080.
