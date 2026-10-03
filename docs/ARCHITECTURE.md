# Architecture

Status: the layering below is implemented as project references. The runtime path is the target design and is **not running**.

## Implemented layout

```text
TaskFlow.Api  ----------+
                        |
TaskFlow.Worker --------+--> TaskFlow.Infrastructure --> TaskFlow.Application --> TaskFlow.Domain
                        |                |
                        +--> TaskFlow.Application ------+
```

- `TaskFlow.Domain` has no project references. It will hold `Job`, `JobAttempt`, statuses, and transition rules.
- `TaskFlow.Application` references Domain only. It will hold use cases and handler contracts.
- `TaskFlow.Infrastructure` references Application and Domain. SQL Server and RabbitMQ will live here.
- `TaskFlow.Api` and `TaskFlow.Worker` are composition roots. They reference Application and Infrastructure so they can register implementations. They must not contain business rules.

`tests/TaskFlow.UnitTests/ProjectLayoutTests.cs` fails if that graph changes.

There is no `Class1` placeholder and no fake job store.

## Target runtime (planned)

```text
Client
  |  POST /api/v1/jobs
  v
API
  |-- insert Job (Pending, then Queued) in SQL Server
  |-- publish { jobId, type, correlationId } to RabbitMQ
  v
exchange taskflow.jobs  --routing key job.process-->  queue taskflow.jobs.process
  |
  +--> Worker 1 --+
  |               +--> load job, claim, run IJobHandler, write attempt, ack
  +--> Worker 2 --+
```

Workers are competing consumers. RabbitMQ decides which idle worker receives a message. The database decides which worker wins the claim if the same job id is delivered twice.

## Messaging shape (planned)

| Piece | Value | Why |
| --- | --- | --- |
| Exchange | `taskflow.jobs` (durable, direct) | One stream of work. A direct exchange is enough. |
| Queue | `taskflow.jobs.process` (durable) | Survives a broker restart. |
| Routing key | `job.process` | A single key until there is a real reason to split work. |
| Body | Job id and small metadata | SQL Server holds the payload. The queue stays small. |
| Ack | Manual | Ack after the outcome is stored. |
| Prefetch | Small, configured | Stops one worker from hoarding jobs. Exact number is a Phase 3 choice. |
| Concurrency | Several worker processes, modest in-process parallelism | Scale out by adding a process first. |

Redelivery: if the worker dies before ack, RabbitMQ delivers the same message again. The consumer treats that as at-least-once, loads the job, and does nothing if the job is already terminal.

Retries do not use an immediate requeue. A retryable failure sets `NextAttemptAt` and a scheduler publishes the job again when it is due. That is how backoff exists without a delay plugin.

## State machine (planned)

Normal:

`Pending -> Queued -> Processing -> Succeeded`

Retry:

`Processing -> RetryScheduled -> Queued -> Processing`

Permanent failure:

`Processing -> Failed`

Attempts exhausted:

`Processing` or `RetryScheduled -> DeadLettered`

A job, a RabbitMQ message, and an attempt are different things. One job has many attempts. One attempt is caused by a delivery, but a delivery of an already finished job creates no new attempt.

## Failure window (planned, accepted for the core)

The API will commit the job row and then publish. Those are not one transaction. If the process dies in between, SQL Server has a job and RabbitMQ does not. That job would sit until something republishes it.

Phase 8 may add a transactional outbox. Until then the docs must keep this limitation visible. The outbox would not remove duplicate delivery; handlers still have to be idempotent.

## What is deliberately absent

- No distributed lock service
- No RabbitMQ delay plugin
- No outbox table
- No auth middleware
- No health endpoints yet (`/health/live` and `/health/ready` are Phase 6)
