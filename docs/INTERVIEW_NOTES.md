# Interview notes

Answers below are split into **today** (what the code does) and **target** (the accepted design). Use the target only after the matching phase is implemented. Do not quote a metric. This project has measured none.

## How I would explain this in an interview

### Today (30–60 seconds)

TaskFlow is a learning project I am building: a background job queue. The API will accept a job, store it in SQL Server, and publish a small message to RabbitMQ. A separate worker will do the work, ack the message only after the result is saved, and retry failures with backoff. Right now the repository is the skeleton. The projects compile, the dependency direction is tested, and `GET /` says job processing is off. I have not built the queue yet. I wrote the design down first so the failure cases (crash before ack, duplicate delivery, publish after the database commit) are explicit before the code hides them.

### Target, not earned yet (about 60 seconds)

Do not recite this as current experience.

The API is the front door. It writes the job to SQL Server, which is the system of record, and publishes a job id to RabbitMQ. Workers compete for that queue. A worker loads the job, claims it with a conditional update, runs a handler, and writes an attempt row. It acks only after that write. If it dies first, RabbitMQ redelivers. If the job is already succeeded, the next worker acks and stops. Retries are not an immediate requeue: a retryable error sets `NextAttemptAt` with exponential backoff and jitter, and a scheduler publishes the job again when it is due. Permanent errors stop. I do not call this exactly-once. The remaining hole is a crash between the SQL commit and the publish, which an outbox would close later.

## System design

### Why a queue?

- Short answer (today): The code does not have a queue yet. The reason it will is so the HTTP request can return without doing the slow work, and so a worker crash does not lose the request body.
- TaskFlow example: `POST /api/v1/jobs` is specified in `docs/API_CONTRACT.md` and returns 404 today.
- Follow-up: Why not a background thread in the API?
- Deeper answer: A thread inside the API dies with the API process, does not give you competing consumers, and has no ack. A separate worker plus a durable queue keeps the work after the process is gone. Cost: two stores that can briefly disagree.

### What happens when a worker dies?

- Short answer (today): Nothing queue-related. The skeleton worker is a `BackgroundService` that waits until cancellation. There is no message to lose.
- Target: If it dies before ack, the broker redelivers. If it dies after the status commit and before ack, the next consumer sees a terminal job and acks.
- Follow-up: What if it dies after the handler side effect and before the commit?
- Deeper answer: That attempt can run again. That is why the design is at-least-once, not exactly-once.

## ASP.NET Core

### Why is the API a separate project from the worker?

- Short answer: So the HTTP host and the consumer host can scale and crash independently. Both are composition roots over the same application and infrastructure projects.
- TaskFlow example: `src/TaskFlow.Api/Program.cs` and `src/TaskFlow.Worker/Program.cs`.
- Follow-up: Could one process host both?
- Deeper answer: Yes, and that is a reasonable later simplification for a tiny deployment. The learning goal is the boundary, so they are separate projects from the start. The API does not reference the worker.

## C#

### Why are the layer names `static readonly` instead of `const`?

- Short answer: A `const` string is inlined. The compiler then drops the project reference, and the layout test sees an empty reference list. A `static readonly` field keeps the assembly reference.
- TaskFlow example: `DomainAssembly.Name`, used by `ProjectLayoutTests`.
- Follow-up: Does that matter for real types?
- Deeper answer: No. Once `Job` lives in Domain and Application uses it, the reference exists because the type is used. The marker types are a Phase 0 stand-in.

## SQL Server

### Why is the database the system of record?

- Short answer (today): It is not connected yet. ADR-006 says the queue is a bad place to store status, because messages are deleted and redelivered.
- Target example: `Jobs` and `JobAttempts` in `docs/DATABASE.md`.
- Follow-up: What is the unique constraint for?
- Deeper answer: `(JobId, AttemptNumber)` stops two workers from inserting the same attempt.

## EF Core

### Why code first?

- Short answer (today): No model exists.
- Target: The domain types are the source, and migrations are generated from them so the schema is reviewed in git.
- Follow-up: Where will migrations live?
- Deeper answer: Infrastructure. Domain will not reference EF Core.

## Dapper

### Why Dapper and EF Core?

- Short answer (today): Neither package is referenced.
- Target: EF Core for writes and migrations. Dapper for a read-heavy list if the LINQ query gets awkward. Not two write paths.
- Follow-up: Is that premature?
- Deeper answer: Yes to add it now. The prompt reserves it for the dashboard-style read. It stays out until that query exists.

## RabbitMQ

### What does RabbitMQ do here?

- Short answer (today): Nothing. The client is not installed. ADR-002 records the choice.
- Target: Durable direct exchange `taskflow.jobs`, queue `taskflow.jobs.process`, routing key `job.process`, manual ack, small messages.
- Follow-up: Why not Kafka?
- Deeper answer: Competing consumers and per-message ack are the lesson. Kafka's log and consumer groups are a different lesson, and the prompt excludes it.

### What does ack mean?

- Short answer (today): No consumer runs, so nothing is acknowledged.
- Target: Ack tells the broker the message can be deleted. We ack after the database has the outcome. Ack is not the same word as "the handler succeeded": a permanent failure is also acked, after the job is `Failed` or `DeadLettered`, so the poison message does not spin.

## Distributed systems and reliability

### Why can a message be processed twice?

- Short answer (today): It cannot, because there is no consumer.
- Target: The worker can finish, crash before ack, and another worker can receive the redelivery. Or two deliveries can overlap before either commits.
- Follow-up: How do you prevent that?
- Deeper answer: You do not fully prevent the delivery. You make the second one safe: a terminal status is acked and skipped; a claim is a conditional update so only one worker moves the row to `Processing`.

### What is at-least-once?

- Short answer: The message is delivered one or more times, never "guaranteed once". TaskFlow has accepted that model in ADR-004 and has not implemented it.
- Follow-up: What would exactly-once require?
- Deeper answer: An atomic side effect and consume, which this stack does not have. An outbox plus idempotent handlers is the practical version, and it is still at-least-once with duplicates suppressed.

### How do retries work? Why backoff and jitter?

- Short answer (today): They do not run.
- Target: Retryable errors schedule `NextAttemptAt`. The delay grows. Jitter spreads workers that failed together so they do not stampede the dependency. Permanent errors skip the schedule.
- Follow-up: Why is immediate requeue dangerous?
- Deeper answer: A down dependency gets the full arrival rate again, immediately, from every worker. That is a retry storm.

### What if SQL Server commits and the publish fails?

- Short answer (today): The path does not exist.
- Target: The row exists and the queue does not have the job. The API must surface the error. A later outbox publishes from the same transaction as the insert. Documented in ADR-007 as deferred.

## Testing

### What do the tests prove?

- Short answer: The layer graph, and that the API starts and tells the truth about being a skeleton.
- TaskFlow example: `ProjectLayoutTests`, `ApiSkeletonTests`.
- Follow-up: Where will the race tests live?
- Deeper answer: Integration tests against real SQL Server and RabbitMQ (Testcontainers), Phase 5. Not against an in-memory fake of RabbitMQ.

## Docker

### How will you run it?

- Short answer (today): `dotnet test` and `dotnet run`. Compose is described in `docker/README.md` and does not exist.
- Target: `docker compose up` brings SQL Server, RabbitMQ, the API, and two workers.
- Follow-up: Why two workers in compose?
- Deeper answer: To show competing consumers. Two consumers do not mean twice the throughput for every workload.

## Observability

### How will you trace a job?

- Short answer (today): You cannot. There is no job id in the logs.
- Target: Correlation id on the request, the row, the message, and the Serilog properties listed in `docs/OBSERVABILITY.md`.
- Follow-up: Why not log the payload?
- Deeper answer: Payloads can be large and sensitive. The id is enough to load the row.
