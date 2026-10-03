# Database

Status: **three migrations exist.** `dotnet ef database update` (with `ConnectionStrings__TaskFlow` set in the environment) applies `InitialJobSchema`, `JobClientGeneratedIds`, and `EnableReadCommittedSnapshot`. There is still no retry-scheduler query, so the filtered `(Status, NextAttemptAt)` index is not created yet. `CorrelationId` is stored and not indexed, because the list endpoint does not search by it.

## Engine

SQL Server, EF Core code first, migrations in Infrastructure. Dapper is for selected reads (the job list once it is worth a hand-written query), not for writes.

## Isolation (implemented)

Migration `EnableReadCommittedSnapshot` runs `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE` outside a transaction. Reads under the default `READ COMMITTED` level then see the last committed row version instead of taking shared locks.

Why: before this, `GET /api/v1/jobs/{id}` (Jobs joined to JobAttempts) could deadlock with a worker that was updating the job and inserting an attempt. SQL Server picked one side as the victim (error 1205). The API returned 503, or the worker nacked and the redelivery found the job `Processing` and skipped it. Found by `CompetingConsumersTests`; `JobPersistenceTests.Migrations_turn_on_read_committed_snapshot` keeps it on.

Costs: row versions live in `tempdb`, and `ROLLBACK IMMEDIATE` ends other open transactions on the database while the migration runs. Writes still lock rows, so the Phase 5 compare-and-update claim is unaffected.

The migration cannot run against `master` (SQL Server refuses `ALTER DATABASE CURRENT` on a system database). The integration tests use a database named `TaskFlow` inside the container for that reason.

## Tables (planned)

### Jobs

| Column | Role |
| --- | --- |
| Id | Primary key (uniqueidentifier) |
| Type | Handler key, for example `demo.success` |
| Payload | JSON. Not copied into the queue message |
| Status | The state machine, stored as a number or a constrained string. Not a free-form label |
| AttemptCount | Attempts started, not deliveries received |
| MaxAttempts | Copied from the request or the default policy |
| NextAttemptAt | When a `RetryScheduled` job becomes eligible. Null otherwise |
| LastStartedAt | Start of the latest attempt |
| LastCompletedAt | End of the latest attempt |
| LastError | Short, safe error summary. Not a payload dump |
| CreatedAt, UpdatedAt | Audit |
| CorrelationId | Caller-supplied or generated. Indexed only because the API will filter on it |

### JobAttempts

| Column | Role |
| --- | --- |
| Id | Primary key |
| JobId | Foreign key to Jobs |
| AttemptNumber | 1-based, unique per job |
| StartedAt, CompletedAt | Timing |
| Outcome | Succeeded, retryable failure, permanent failure |
| ErrorType, ErrorMessage | Classification plus a short message |
| Duration | Stored so logs and the row agree |
| WorkerId | Which process ran the attempt |

A unique `(JobId, AttemptNumber)` constraint stops two workers from recording the same attempt number.

## Indexes (planned)

Only these, because a query needs them:

| Index | Query |
| --- | --- |
| Jobs primary key | Load by id |
| `Jobs (Status, NextAttemptAt)` filtered to `RetryScheduled` | Retry scheduler batch. Must not scan every job |
| `Jobs (CreatedAt)` | List newest first |
| `Jobs (Type, CreatedAt)` | Filter the list by type |
| `Jobs (CorrelationId)` | Lookup when the caller passes one |
| `JobAttempts (JobId, AttemptNumber)` unique | History for one job, and the concurrency constraint |

The initial migration creates `IX_Jobs_CreatedAt`, `IX_Jobs_Type_CreatedAt`, and `IX_Jobs_Status_CreatedAt`, because the list query filters and sorts on those columns. It also creates the unique `(JobId, AttemptNumber)` index. The retry-scheduler index and the `CorrelationId` index are still not created. `Duration` is stored as bigint ticks because SQL Server `time` cannot hold every `TimeSpan`.

## Transactions

- Implemented: creating the job row is one transaction. Publishing to RabbitMQ is outside it. The `Queued` update after the confirm is a second transaction.
- Implemented: the worker's claim (`Processing` plus a new attempt row) is one `SaveChanges`. The outcome (`Succeeded` or `Failed` plus the closed attempt) is another, and it commits before the message is acknowledged.
- Planned (Phase 5): the claim becomes conditional on `Status = Queued`. Zero rows means another worker won.
- Planned (Phase 4): the scheduler conditionally moves a due `RetryScheduled` row to `Queued` and then publishes. A second pass that updates zero rows does not publish.

## Concurrency (planned)

Optimistic, compare-and-update on status. No application lock table. A row version column is optional if a later update needs to detect lost writes beyond the status predicate.

## Outbox (not in the first schema)

`OutboxMessages` is Phase 8. Do not add it early.
