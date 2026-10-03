# Decisions

These ADRs record choices that are already made. The implementation status says whether the code exists. An accepted ADR is not a claim that the feature runs.

## ADR-001: Asynchronous worker architecture

- Status: Accepted. Implementation: done for one worker process (Phase 3 task 2). `TaskFlow.Worker` runs `RabbitMqJobConsumer`.
- Context: Some work is slow or flaky. Doing it inside the HTTP request ties the client's latency to that work and loses the work if the request process dies mid-way.
- Decision: The API records the job and returns. A separate worker process performs the handler.
- Alternatives: Run the work in the request. Use `BackgroundService` inside the API process only. Use a hosted queue service.
- Tradeoffs: A separate worker can scale and crash without taking down the API. It adds a second deployable and a failure window between the database and the broker.
- Consequences: `TaskFlow.Worker` is its own project. In-process hosted services inside the API are not the design.

## ADR-002: RabbitMQ as the transport

- Status: Accepted. Implementation: publisher and topology (Phase 3 task 1) and consumer (Phase 3 task 2) done. `RabbitMQ.Client` 7.2.2 pinned on 2026-10-03 after re-checking NuGet.
- Context: Workers need a durable queue with competing consumers and explicit ack.
- Decision: RabbitMQ. One direct exchange, one durable queue, one routing key. See `docs/ARCHITECTURE.md`.
- Alternatives: A SQL table polled by workers. Kafka. A cloud queue. An in-memory channel.
- Tradeoffs: RabbitMQ matches ack, redelivery, and competing consumers without operating a cluster of brokers for this project. It is another process to run. A SQL-only queue would teach less about messaging and would make "the database is down" and "the queue is down" the same failure.
- Consequences: Phase 3 adds `RabbitMQ.Client`. On 2026-10-03 the newest stable package version was 7.2.2. Confirm that against [the RabbitMQ .NET guide](https://www.rabbitmq.com/docs/dotnet) and NuGet before locking it. Do not vendor a client in Phase 0.

## ADR-003: Manual acknowledgements

- Status: Accepted. Implementation: done. `RabbitMqJobConsumer` consumes with `autoAck: false` and acks only after `JobProcessor` returns, which is after the outcome save. The "success then crash before ack" case is covered by the duplicate-delivery test, which republishes a message for a `Succeeded` job; a real process crash is not tested yet.
- Context: Auto-ack deletes the message when it is delivered, before the handler runs. A crash then loses the work.
- Decision: Manual ack. Ack after the attempt outcome is committed. Do not ack and then do the work.
- Alternatives: Auto-ack. Ack at the start of the handler.
- Tradeoffs: Manual ack can deliver a message more than once. That is the reason idempotency exists. Auto-ack is simpler and drops work on a crash.
- Consequences: Worker code must have one obvious ack site. Tests must cover "success then crash before ack".

## ADR-004: At-least-once processing

- Status: Accepted. Implementation: partial. A redelivery for a job that is no longer `Queued` is acked without running the handler. The conditional claim between two workers is Phase 5.
- Context: Manual ack plus crashes means redelivery. Two workers can observe the same job id.
- Decision: The system promises at-least-once delivery and idempotent handling of terminal jobs. It does not promise exactly-once execution.
- Alternatives: Pretend exactly-once. Ignore duplicates.
- Tradeoffs: Callers and handlers must tolerate a duplicate delivery. The worker must not redo a `Succeeded` or `DeadLettered` job. A duplicate can still happen if the handler succeeded and the process died before the status commit.
- Consequences: Documentation and interview answers use "at-least-once". The word "exactly-once" is not used for this design.

## ADR-005: Retry policy with exponential backoff and jitter

- Status: Accepted. Implementation: not started (Phase 4).
- Context: Immediate requeue storms a failing dependency. A fixed delay synchronizes every worker.
- Decision: A configurable policy classifies failures. Retryable failures use exponential backoff with jitter. The illustrative schedule is immediate, ~5s, ~30s, ~2m, ~10m, then dead-letter. Those numbers are configuration, not constants buried in the handler.
- Alternatives: Infinite requeue. Fixed delay. RabbitMQ's delayed-message plugin. Dead-letter on the first failure.
- Tradeoffs: Backoff adds a scheduler and a `NextAttemptAt` column. It protects downstream systems. Jitter adds a little randomness so retries do not align.
- Consequences: Permanent failures do not use this schedule. They go to `Failed`. Exhausted retryable failures go to `DeadLettered`.

## ADR-006: Database-backed job state

- Status: Accepted. Implementation: `Jobs` and `JobAttempts` are created by migration `InitialJobSchema`. A saved job becomes `Queued` only after the publish is confirmed (ADR-008).
- Context: The queue message is not a good system of record. It can be redelivered, and it should stay small.
- Decision: SQL Server stores the job, the attempts, and the next retry time. RabbitMQ stores only enough to find the job.
- Alternatives: Keep status only in the message headers. Use the queue depth as the status API.
- Tradeoffs: Two stores can disagree for a moment (see the publish window). The API can list jobs after the message is gone. Operators can see attempts.
- Consequences: Every terminal transition is a database write. The queue is not queried to answer "what happened to this job?".

## ADR-007: Transactional outbox

- Status: **Deferred.** Not accepted as current scope.
- Context: ADR-006 leaves a gap between the SQL commit and the publish.
- Decision: Do not build an outbox until Phases 0–7 are stable. If it is added, the job row and the outbox row commit together, a publisher sends the outbox row, and consumers stay idempotent. The outbox does not create exactly-once processing.
- Alternatives: Build it now. Ignore the gap.
- Tradeoffs: Waiting keeps the first version understandable. The gap remains a real bug class until Phase 8.
- Consequences: API docs must describe the gap. Do not hide it behind a retry loop that looks like a transaction.

## ADR-008: Store first, publish with a confirm, then mark Queued

- Status: Accepted. Implementation: done (Phase 3 task 1). `JobSubmissionService`, `RabbitMqJobPublisher`.
- Context: Without an outbox, the row and the message cannot commit together. Something has to go first, and "published" needs a precise meaning.
- Decision: Commit the row as `Pending`. Publish on a channel with publisher confirmations and tracking, with `mandatory: true` and a persistent delivery mode. Only after the confirm, call `Job.MarkQueued` and save. Any failure before the confirm leaves the row `Pending` and returns 503 with the job id. If RabbitMQ is not configured at all, reject the submission before storing anything.
- Alternatives: Publish first, then insert (a worker could receive an id with no row). Fire-and-forget publish without confirms (the API would say `Queued` for a message the broker never accepted). Mark `Queued` in the same save as the insert (the row would lie whenever the publish fails). Store and return 201 `Pending` when RabbitMQ is not configured (a job nobody will ever publish).
- Tradeoffs: A confirm costs a broker round-trip per request. A channel per publish is simple and avoids sharing a channel across threads, but costs another round-trip. A failed publish leaves an orphan `Pending` row, and a failed second save leaves a message for a `Pending` row. Both are visible in the 503 body and in `docs/ARCHITECTURE.md`.
- Consequences: The consumer must handle a delivery whose row is still `Pending`. ADR-009 does that. A republisher or the Phase 8 outbox is the later fix for orphan `Pending` rows.

## ADR-009: Consumer outcome rules

- Status: Accepted. Implementation: done (Phase 3 task 2). `JobProcessor`, `RabbitMqJobConsumer`.
- Context: The worker receives a job id that may be new, a redelivery, a duplicate, or garbage, and SQL Server may be down. Each case needs a settle call that neither loses work nor loops forever.
- Decision:
  - The row decides, not the message. `Pending` is promoted with `MarkQueued`, because a delivered message proves the publish happened. `Queued` is claimed and run. Every other status is acked without running the handler.
  - The handler's result decides the saved status (`Succeeded` or `Failed`). The message is acked after that save.
  - Until Phase 4, a handler exception is a permanent failure, recorded with the exception type name and not the message text.
  - An unreadable body is rejected without requeue.
  - A SQL Server outage is nacked with requeue after `DatabaseRetryDelay`. That rate-limited requeue is for infrastructure outages only, not for job failures, and it is not the retry policy.
  - Any other unexpected processing error is rejected without requeue and logged.
  - Prefetch defaults to 1 and the consumer runs one message at a time.
- Alternatives:
  - Ack and skip a `Pending` delivery. That leaves a job that will never run.
  - Requeue on handler failure. That causes a hot loop, which the messaging rules forbid.
  - Leave a message unacked while SQL Server is down. With prefetch 1 that blocks the worker without telling RabbitMQ anything.
  - Drop the message while SQL Server is down. That loses the only pointer to the job.
- Tradeoffs:
  - During a SQL outage, every worker requeues once per `DatabaseRetryDelay`. That is noisy but bounded.
  - A crash after the claim leaves a `Processing` row that later deliveries skip, so the job is stuck until Phase 5.
  - Treating every exception as permanent is wrong for transient errors until Phase 4.
- Consequences: The ack has one site. A job does not run for a delivery whose row says it is already running or finished. Phase 5 replaces the plain claim save with a compare-and-update and adds recovery for stuck `Processing` rows.
