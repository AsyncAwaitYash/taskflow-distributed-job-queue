# Architecture

Status: the layering below is implemented as project references. The runtime path runs end to end: the API stores, publishes with a confirm, and marks `Queued`; one or more workers consume, claim, run a handler, save the outcome, and ack. A retryable failure is saved as `RetryScheduled` or `DeadLettered` and acked. The scheduler that republishes a due retry, and the conditional claim, are **not built**.

## Implemented layout

```text
TaskFlow.Api  ----------+
                        |
TaskFlow.Worker --------+--> TaskFlow.Infrastructure --> TaskFlow.Application --> TaskFlow.Domain
                        |                |
                        +--> TaskFlow.Application ------+
```

- `TaskFlow.Domain` has no project references. It holds `Job`, `JobAttempt`, statuses, and transition rules.
- `TaskFlow.Application` references Domain only. It holds the use cases (`JobSubmissionService`, `JobQueryService`, `JobProcessor`) and the handlers (`IJobHandler`, `JobHandlerRegistry`).
- `TaskFlow.Infrastructure` references Application and Domain. SQL Server (EF Core) and RabbitMQ (publisher and consumer) live here.
- `TaskFlow.Api` and `TaskFlow.Worker` are composition roots. They reference Application and Infrastructure so they can register implementations. They must not contain business rules.

`tests/TaskFlow.UnitTests/ProjectLayoutTests.cs` fails if that graph changes.

There is no `Class1` placeholder and no fake job store.

## Runtime

```text
Client
  |  POST /api/v1/jobs
  v
API (implemented)
  |-- 1. insert Job as Pending, commit
  |-- 2. publish { jobId, type, correlationId }, wait for the broker confirm
  |-- 3. Job.MarkQueued, save
  v
exchange taskflow.jobs  --routing key job.process-->  queue taskflow.jobs.process   (implemented)
  |
  +--> Worker 1 (implemented) --> load job, claim, run IJobHandler, write outcome, ack
  +--> Worker 2 (implemented: the same TaskFlow.Worker code as a second process, its own WorkerId)
```

Competing consumers are proven by `CompetingConsumersTests` (two hosts, two connections) and by a manual run of two processes (`docs/DEMO.md`). Each message goes to one consumer. With prefetch 1, a busy worker is not offered a second message, so slow jobs spread across idle workers.

The worker side:

- `RabbitMqJobConsumer` (Infrastructure, a `BackgroundService`) consumes with `autoAck: false` and `BasicQos(prefetch)`. It parses the `JobMessage`, opens a DI scope, calls `JobProcessor`, and settles the delivery. There is one ack site.
- `JobProcessor` (Application) loads the row and decides from the row, not the message:

| Row status on delivery | Action | Settle |
| --- | --- | --- |
| not found | nothing | ack |
| `Pending` | `MarkQueued`, then as `Queued` | ack after the outcome save |
| `Queued` | `StartProcessing` and save (the claim), run the handler, `CompleteSuccessfully` or `FailPermanently` and save | ack |
| `Processing`, `RetryScheduled`, `Succeeded`, `Failed`, `DeadLettered` | nothing; the handler does not run | ack |

| Failure | Settle |
| --- | --- |
| Body is not a `JobMessage` | `BasicReject(requeue: false)`. No dead-letter exchange, so it is dropped |
| `JobDatabaseUnavailableException` | wait `DatabaseRetryDelay` (5 s), then `BasicNack(requeue: true)` |
| Any other exception from processing | `BasicReject(requeue: false)`, logged as an error |
| The ack itself fails (channel lost) | RabbitMQ redelivers; the row is terminal, so it is skipped |

A handler result or exception never decides the settle call. It decides the saved status, and the message is acked after that save.

The publish code lives in `src/TaskFlow.Infrastructure/Messaging`. Application sees only `IJobPublisher` and `JobMessage`, so it has no RabbitMQ types.

- `RabbitMqConnectionProvider` is a singleton. It opens one connection lazily, with automatic recovery, and declares the topology on that connection before handing it out. The API starts even when RabbitMQ is down.
- `RabbitMqJobPublisher` opens a channel for each publish with publisher confirmations and confirmation tracking. `BasicPublishAsync` then completes only after the broker acks, and throws on a nack or an unroutable return (`mandatory: true`).
- `TaskFlow:RabbitMq:PublishTimeout` (default 5 seconds) bounds the connect, the declare, and the confirm wait. A timeout is a publish failure.

Workers are competing consumers. RabbitMQ decides which idle worker receives a message. Today the claim is a plain save after reading `Queued`. The compare-and-update claim that decides between two workers holding the same job id is Phase 5.

## Messaging shape

| Piece | Value | Why | State |
| --- | --- | --- | --- |
| Exchange | `taskflow.jobs` (durable, direct) | One stream of work. A direct exchange is enough. | Implemented |
| Queue | `taskflow.jobs.process` (durable, not exclusive, not auto-delete) | Survives a broker restart. | Implemented |
| Routing key | `job.process` | A single key until there is a real reason to split work. | Implemented |
| Body | `{ jobId, type, correlationId }` as JSON | SQL Server holds the payload. The queue stays small. | Implemented |
| Properties | Persistent, `application/json`, `MessageId` = job id, `CorrelationId`, `Type` | A persistent message in a durable queue survives a broker restart. | Implemented |
| Publish | Confirms on, `mandatory: true` | "Queued" means the broker accepted and routed it. | Implemented |
| Ack | Manual | Ack after the outcome is stored. | Implemented |
| Prefetch | `TaskFlow:Worker:PrefetchCount`, default 1, range 1–50 | Stops one worker from hoarding jobs. | Implemented |
| Concurrency | One message at a time per worker process. Scale out by adding a process. | Simple ordering and one ack site. | Implemented, two processes tested |

Redelivery: if the worker dies before ack, RabbitMQ delivers the same message again. The consumer loads the job and does nothing if the row is no longer `Queued` (or `Pending`). That is tested for a `Succeeded` job. A worker that dies after the claim leaves the row `Processing`, and the redelivery skips it, so the job is stuck until Phase 5 adds recovery.

Retries do not use an immediate requeue. A retryable failure sets `NextAttemptAt` through `RetryBackoffPolicy` and the worker acks. The scheduler that publishes the job when it is due is the next task, so a `RetryScheduled` job waits today.

## State machine

Implemented in `Job` and `JobTransitions`. The API drives `Pending -> Queued`. The worker drives `Queued -> Processing -> Succeeded`, `Processing -> Failed`, and `Processing -> RetryScheduled` or `DeadLettered`, and promotes a delivered `Pending` row to `Queued`.

`MarkQueued` publishes a `Pending` or `RetryScheduled` job. `RetryManually` is the only way back from `Failed` or `DeadLettered`, and it does not reset `AttemptCount` or `MaxAttempts`. `RecordRetryableFailure` schedules `NextAttemptAt` when attempts remain, and moves straight to `DeadLettered` when `AttemptCount` has reached `MaxAttempts`. `RetryBackoffPolicy` supplies the delay. The scheduler that calls `MarkQueued` on a due row is the next task.

Normal:

`Pending -> Queued -> Processing -> Succeeded`

Retry:

`Processing -> RetryScheduled -> Queued -> Processing`

Permanent failure:

`Processing -> Failed`

Attempts exhausted:

`Processing` or `RetryScheduled -> DeadLettered`

Manual retry, without changing the attempt budget:

`Failed` or `DeadLettered -> Queued`

A job, a RabbitMQ message, and an attempt are different things. One job has many attempts. One attempt is caused by a delivery, but a delivery of an already finished job creates no new attempt.

## Failure windows (implemented, accepted for the core)

The API commits the job row, publishes, and then commits the `Queued` status. Those are three steps, not one transaction. There are two gaps:

1. **Stored, not published.** The publish fails or the process dies before the confirm. SQL Server has a `Pending` row and RabbitMQ has no message. The API returns 503 with the `jobId` when it is still alive to answer. Nothing republishes `Pending` rows yet.
2. **Published, not marked.** The broker confirms, then the `Queued` save fails or the process dies. RabbitMQ has a message for a row that says `Pending`. The API returns 503 with the `jobId` when it can. The worker closes this gap: it promotes the `Pending` row with `MarkQueued` and runs the job (`WorkerTests.A_message_for_a_pending_row_promotes_and_runs_the_job`).

A broker that accepts the message and then loses the confirm (connection dropped mid-wait) is reported as a failure, but the message may exist. That is a duplicate-delivery case, not a lost job.

Phase 8 may add a transactional outbox. Until then the docs must keep this limitation visible. The outbox would not remove duplicate delivery; handlers still have to be idempotent.

## What is deliberately absent

- No distributed lock service
- No RabbitMQ delay plugin
- No outbox table
- No auth middleware
- No health endpoints yet (`/health/live` and `/health/ready` are Phase 6)
