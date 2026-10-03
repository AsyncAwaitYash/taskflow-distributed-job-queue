# Decisions

These ADRs record choices that are already made. The implementation status says whether the code exists. An accepted ADR is not a claim that the feature runs.

## ADR-001: Asynchronous worker architecture

- Status: Accepted. Implementation: not started (Phase 3).
- Context: Some work is slow or flaky. Doing it inside the HTTP request ties the client's latency to that work and loses the work if the request process dies mid-way.
- Decision: The API records the job and returns. A separate worker process performs the handler.
- Alternatives: Run the work in the request. Use `BackgroundService` inside the API process only. Use a hosted queue service.
- Tradeoffs: A separate worker can scale and crash without taking down the API. It adds a second deployable and a failure window between the database and the broker.
- Consequences: `TaskFlow.Worker` is its own project. In-process hosted services inside the API are not the design.

## ADR-002: RabbitMQ as the transport

- Status: Accepted. Implementation: not started. Package not referenced.
- Context: Workers need a durable queue with competing consumers and explicit ack.
- Decision: RabbitMQ. One direct exchange, one durable queue, one routing key. See `docs/ARCHITECTURE.md`.
- Alternatives: A SQL table polled by workers. Kafka. A cloud queue. An in-memory channel.
- Tradeoffs: RabbitMQ matches ack, redelivery, and competing consumers without operating a cluster of brokers for this project. It is another process to run. A SQL-only queue would teach less about messaging and would make "the database is down" and "the queue is down" the same failure.
- Consequences: Phase 3 adds `RabbitMQ.Client`. On 2026-10-03 the newest stable package version was 7.2.2. Confirm that against [the RabbitMQ .NET guide](https://www.rabbitmq.com/docs/dotnet) and NuGet before locking it. Do not vendor a client in Phase 0.

## ADR-003: Manual acknowledgements

- Status: Accepted. Implementation: not started.
- Context: Auto-ack deletes the message when it is delivered, before the handler runs. A crash then loses the work.
- Decision: Manual ack. Ack after the attempt outcome is committed. Do not ack and then do the work.
- Alternatives: Auto-ack. Ack at the start of the handler.
- Tradeoffs: Manual ack can deliver a message more than once. That is the reason idempotency exists. Auto-ack is simpler and drops work on a crash.
- Consequences: Worker code must have one obvious ack site. Tests must cover "success then crash before ack".

## ADR-004: At-least-once processing

- Status: Accepted. Implementation: not started.
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

- Status: Accepted. Implementation: `Jobs` and `JobAttempts` are created by migration `InitialJobSchema`. RabbitMQ is still not connected, so a saved job stays `Pending`.
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
