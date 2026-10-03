# Database

Status: **the first migration exists.** `dotnet ef database update` creates `Jobs` and `JobAttempts`. There is still no retry-scheduler query, so the filtered `(Status, NextAttemptAt)` index is not created yet. `CorrelationId` is stored and not indexed, because the list endpoint does not search by it.

## Engine

SQL Server, EF Core code first, migrations in Infrastructure. Dapper is for selected reads (the job list once it is worth a hand-written query), not for writes.

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

## Transactions (planned)

- Creating the job row is one transaction. Publishing to RabbitMQ is outside it until an outbox exists.
- The scheduler conditionally moves a due `RetryScheduled` row to `Queued` and then publishes. A second pass that updates zero rows does not publish.
- The worker claims a `Queued` row by setting `Processing`. Zero rows means another worker won.
- Recording the attempt and moving to `Succeeded`, `Failed`, `RetryScheduled`, or `DeadLettered` commits in one transaction before the message is acknowledged.

## Concurrency (planned)

Optimistic, compare-and-update on status. No application lock table. A row version column is optional if a later update needs to detect lost writes beyond the status predicate.

## Outbox (not in the first schema)

`OutboxMessages` is Phase 8. Do not add it early.
