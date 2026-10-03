# TaskFlow learning guide

This is the textbook for the repository. If a sentence describes behavior, the code must do that behavior. Sections marked **Planned** are the design. They are not features you can demo.

## What is TaskFlow?

**Status: jobs are stored in SQL Server, published to RabbitMQ, and run by one worker process that acks after saving the outcome. Retries and a second worker are not built.**

TaskFlow will be a small background-job system. A client will ask an API to do something that should not block the HTTP call, such as "send this email" or "generate this report". The API will remember the job. A worker process, running separately, will do it later.

You are building it to learn two ideas well enough to explain in an interview:

1. Asynchronous processing with a message queue.
2. Reliable processing: acknowledgements, retries, idempotency, and what happens when a worker dies.

It is not a SaaS product. Swagger will be the UI. There is no React app.

Today `POST /api/v1/jobs` inserts a `Pending` row, publishes the job id to RabbitMQ, waits for the broker to confirm, and then marks the row `Queued`. `TaskFlow.Worker` takes the message from `taskflow.jobs.process`, loads the row, runs the handler for the job type, saves `Succeeded` or `Failed`, and acks. The retry statuses (`RetryScheduled`, `DeadLettered`) are reached only in unit tests until Phase 4.

## The picture we are building (planned)

```text
Client
  |
  |  POST /api/v1/jobs
  v
API                         remembers the request and returns quickly
  |
  +--> SQL Server           the durable record of the job
  |
  +--> RabbitMQ             a note that says "please work on this job id"
         |
         v
      Worker                picks up the note, loads the job, does the work
         |
         v
      Job handler           the actual email / report / demo behavior
         |
         v
      SQL Server            writes success, retry, or failure
```

A second worker can run at the same time. RabbitMQ gives each message to one of them. That is competing consumers, not "every worker does every job".

### Why each box exists (planned)

| Box | Why it will exist |
| --- | --- |
| API | The front door. It validates input and returns an id. It should not do the slow work. |
| SQL Server | The system of record. You can ask "what happened to job 42?" after the queue message is gone. |
| RabbitMQ | The handoff. It holds work until a worker is free, and it can hand the same work out again if the worker disappears before saying "I finished". |
| Worker | The process that is allowed to be slow. It can crash without crashing the API. |
| Handler | The business step, separated from the queue code. `demo.success` and `demo.permanent-failure` are handlers. The queue does not know how they work. |

## What asynchronous processing means (planned)

Synchronous means the caller waits while the work happens. The HTTP request stays open until the email is sent.

Asynchronous, here, means the API records the job and returns. The work happens on another timeline, in another process. The caller has an id and can ask for status later. The caller does not have the result in the HTTP response.

That split is the point of a background job. It is also why "the API returned 201" and "the job succeeded" are different events. The first one will exist in Phase 2. The second one will exist in Phase 3.

## What RabbitMQ does here

RabbitMQ is the transport, not the database.

Implemented (see `src/TaskFlow.Infrastructure/Messaging`):

- Exchange `taskflow.jobs` (durable, direct)
- Queue `taskflow.jobs.process` (durable)
- Routing key `job.process`
- Message body: `{ jobId, type, correlationId }`, not the payload
- Publisher confirms and `mandatory: true`
- Manual acknowledgement by the worker, prefetch 1

The worker pulls a message, loads the job from SQL Server, does the work, saves the outcome, and only then acknowledges. If the worker dies first, RabbitMQ delivers the message again. That second delivery is redelivery, not a new job.

`RabbitMQ.Client` 7.2.2 is pinned. It was still the newest stable release on NuGet on 2026-10-03.

## What you will learn

| Phase | You will be able to point at code and say |
| --- | --- |
| 0 (done) | How the solution is cut into layers, and why the docs are part of the project |
| 1 (done) | What a job is, what an attempt is, and which status changes are legal |
| 2 (done) | How an HTTP request becomes a row |
| 3 (tasks 1–3 done) | How a message is published, confirmed, consumed, and acknowledged. Two competing workers come next |
| 4 | How retries, backoff, jitter, and dead-lettering differ |
| 5 | Why a duplicate message is normal, and how a claim prevents two workers from both running one job |
| 6 | How to log a job without logging its payload, and how to run the stack in Docker |
| 7 | How the failure tests prove the story |
| 8 (optional) | How an outbox closes the "saved but not published" gap, and what it still does not guarantee |

## Concept master index

"Designed" means an ADR or a design doc chose it. "Implemented" means the code does it.

| Concept | State | Where it lives |
| --- | --- | --- |
| Layered architecture | Implemented | Project references, `ProjectLayoutTests` |
| Dependency injection | Implemented, narrowly | `TaskFlow.Worker/Program.cs` registers `Worker` |
| Structured log placeholders | Implemented, narrowly | The skeleton log lines. Serilog is not installed |
| Asynchronous processing | Designed | This guide, ADR-001 |
| Background jobs | Implemented | API stores and publishes. `TaskFlow.Worker` runs the handler |
| Job state machine | Implemented | `JobTransitions`, `JobLifecycleTests` |
| Message queues | Implemented, publish side | `RabbitMqTopology`, ADR-002 |
| RabbitMQ | Implemented, publish side | `src/TaskFlow.Infrastructure/Messaging`, `JobPublishingTests` |
| Producer | Implemented | `RabbitMqJobPublisher`, called by `JobSubmissionService` |
| Publisher confirms | Implemented | `RabbitMqJobPublisher`, ADR-008 |
| Consumer | Implemented | `RabbitMqJobConsumer`, `WorkerTests` |
| Competing consumers | Designed | Two workers, one queue. Phase 3 task 4 |
| Acknowledgement | Implemented | Ack after the outcome save. ADR-003, ADR-009 |
| Prefetch | Implemented | `TaskFlow:Worker:PrefetchCount`, default 1 |
| Redelivery | Implemented, skip rule only | A non-`Queued` row is acked without running. ADR-009 |
| Retries | Designed | ADR-005 |
| Exponential backoff | Designed | ADR-005 |
| Jitter | Designed | ADR-005 |
| Dead lettering | Designed | Status `DeadLettered`, not a second RabbitMQ queue in v1 |
| Idempotency | Partial | Terminal redelivery is skipped. Conditional claim is Phase 5 |
| At-least-once processing | Partial | Manual ack plus the skip rule. ADR-004 |
| Eventual consistency | Implemented, with documented gaps | Store, publish, then mark `Queued`. `ARCHITECTURE.md` failure windows |
| Transactions | Designed | `DATABASE.md` |
| Database concurrency | Designed | Compare-and-update claim |
| Optimistic concurrency | Designed | Status predicate on update |
| Strategy / handler pattern | Implemented | `IJobHandler`, `JobHandlerRegistry` |
| Correlation IDs | Designed | Column plus logs, Phase 6 |
| Health checks | Designed | `/health/live`, `/health/ready`, Phase 6 |
| Docker | Designed | `docker/README.md`, Phase 6 |
| Observability | Designed | `OBSERVABILITY.md` |
| Transactional outbox | Deferred | ADR-007 |

## Common confusions

Job versus attempt is now visible in `JobLifecycleTests`. The queue and the worker are still design-only, so the rest of these confusions are not runnable yet.

### Job vs message

A job is the row you care about: type, payload, status. A message is the broker's copy of "look at this job id". The job outlives the message. Deleting the message (ack) does not delete the job.

### Job vs attempt

A job is the piece of work. An attempt is one try. `JobLifecycleTests.Retryable_failure_schedules_the_next_try_then_succeeds` builds one job with two attempts. The table that will store them does not exist yet.

### Queue vs database

The queue answers "who should work next?". The database answers "what is true about this job?". Status lives in SQL Server. Queue depth is not the status API.

### Retry vs redelivery

Redelivery is the broker's decision: nobody acked, so here is the message again. Retry is TaskFlow's decision: the handler failed in a way we classified as retryable, so we wait until `NextAttemptAt` and publish again. Immediate requeue is not the retry policy. It would tight-loop.

### Acknowledgement vs success

Ack means "broker, you can drop this message". The handler might have succeeded, or the job might have been marked `Failed` on purpose. Both are acked. What is not acked is "I crashed in the middle".

### At-least-once vs exactly-once

At-least-once: the handler may see the job more than once. Exactly-once: it sees it one time, ever. TaskFlow chooses at-least-once plus "if the row is already terminal, do not run the handler". That is not exactly-once. A crash after the side effect and before the row update can still repeat the side effect.

### Synchronous vs asynchronous

Synchronous: the client waits for the work. Asynchronous: the client waits for the API to *record* the work. `201 Created` will mean "recorded", not "finished".

### Transient vs permanent failure

Transient: worth trying later (timeout, simulated `demo.transient-failure`). Permanent: trying again will fail the same way (`demo.permanent-failure`, unknown job type, bad payload). Permanent failures go to `Failed`. They do not sit on the backoff schedule.

### API response vs background completion

The response is the front door. `Succeeded` is the worker's later write. Poll `GET /api/v1/jobs/{id}` for the second one. That route does not exist yet.

### Database state vs queue state

TaskFlow marks `Queued` only after the broker confirms, so the two stores disagree in the other direction. A failed publish leaves a `Pending` row with no message. A failed second save leaves a message for a `Pending` row. Those are different problems. The outbox is the later fix for the first one. It is not built.

## Failure scenarios I understand

Scenarios 8 and 10 are implemented on the publish side (`JobPublishingTests`). Scenarios 2, 3, and 6 are implemented in the worker (`WorkerTests`, `JobProcessorTests`). Scenario 1 is implemented by manual ack, but a real crash is not tested yet. The rest are planned. Do not answer an interview with a planned scenario as if you have shipped it.

### 1. Worker crashes before ack

- What happened: the worker took a message and died before ack. **Planned scenario.**
- Why: the process is gone; the broker never heard "done".
- Planned behavior: RabbitMQ redelivers to a live consumer.
- Tradeoff: the work may run twice. The alternative, auto-ack, loses the work.
- Interview question: Why is manual ack worth the duplicate?

### 2. RabbitMQ redelivers an unacknowledged message

- Implemented behavior: `JobProcessor` loads the job by id and decides from the row, not from the message. Only a `Queued` (or `Pending`) row runs.
- Tradeoff: every consumer must handle a message it has never seen and a message it might have seen.
- Interview question: What do you store in the message if the row is the source of truth?

### 3. Job succeeds but the worker crashes before ack

- Implemented behavior: the row is `Succeeded`. The next delivery sees a terminal status, acks, and does not run the handler. `A_duplicate_message_for_a_succeeded_job_is_acked_without_a_second_attempt` simulates this by republishing the message.
- Tradeoff: this is safe only if the success was committed before the crash.
- Interview question: Which write has to happen before ack?

### 4. Duplicate message reaches another worker

- Planned behavior: conditional claim. One update wins. The loser sees zero rows and acks or backs off according to the state it reads.
- Tradeoff: a compare-and-update is enough. A distributed lock would add a component this project does not need.
- Interview question: Why not a Redis lock?

### 5. Transient failure requires a retry

- Planned behavior: write `RetryScheduled` and `NextAttemptAt`, ack the current message, let the scheduler publish later.
- Tradeoff: the job is quiet for a while. That is the point of backoff.
- Interview question: Why ack if the job is not done?

### 6. Permanent failure should not retry forever

- Implemented behavior: status `Failed`, attempt row records `PermanentFailure` and the error type, message acked. `A_permanent_failure_is_saved_as_failed`.
- Tradeoff: a bug in classification can drop a job that should have been retried. Classification has to be tested.
- Interview question: Give an example of each class in this project.

### 7. Maximum attempts are reached

- Planned behavior: `DeadLettered`, not another delay.
- Tradeoff: a human can still call the manual retry endpoint. The queue will not spin by itself.
- Interview question: Is dead-letter a RabbitMQ queue or a status? In this design it is a status. Say that clearly.

### 8. RabbitMQ is unavailable

- Implemented behavior: the row is already committed as `Pending`. The publish fails or times out, the API returns 503 with `jobId` and `jobStatus: "Pending"`, and the row stays `Pending`. `Unreachable_broker_returns_503_and_leaves_the_stored_job_pending` proves it. If RabbitMQ is not configured at all, the API returns 503 and stores nothing.
- Tradeoff: the client can retry the HTTP call. The submit path has to be careful not to create two jobs for one user action. Idempotency keys are not in v1; `correlationId` is for tracing, not deduplication, unless a later task says otherwise.
- Interview question: What does the client see?

### 9. SQL Server is unavailable

- Behavior: submit fails and nothing is published (implemented). A worker that cannot load or claim the job waits `DatabaseRetryDelay` and nacks with requeue, so the message returns (implemented in `RabbitMqJobConsumer`, not covered by a test that stops SQL Server).
- Tradeoff: not acking when the database is down is correct and can also pile up unacked messages. Prefetch stays small for that reason.
- Interview question: Why is "nack and requeue immediately" still a bad default while SQL is down?

### 10. API stores state but publish fails

- Implemented behavior: the row remains `Pending`, the response is 503 with the `jobId`, and nothing republishes it yet. That is the open gap.
- Tradeoff: honesty instead of a fake "queued" status.
- Interview question: What would an outbox change, and what would it leave the same?

### 11. Retry scheduler sees the same job twice

- Planned behavior: the "due" update is conditional. The second pass updates zero rows and does not publish a second message.
- Tradeoff: a rare double publish can still happen if the process dies after the update and before it records that the publish finished. Consumers are already idempotent for that.
- Interview question: Which duplicate is the scheduler responsible for, and which is the worker responsible for?

## Task: Phase 0 — Foundation and project memory

### What we built

A .NET 10 solution that compiles and tests, plus the documents that describe the system we have not built yet.

Projects:

- `src/TaskFlow.Api` — web host. `GET /` returns a skeleton JSON document.
- `src/TaskFlow.Application` — class library. References Domain.
- `src/TaskFlow.Domain` — class library. References nothing.
- `src/TaskFlow.Infrastructure` — class library. References Application and Domain.
- `src/TaskFlow.Worker` — generic host. Registers one `BackgroundService` that logs and waits.
- `tests/TaskFlow.UnitTests` — locks the reference graph.
- `tests/TaskFlow.IntegrationTests` — boots the API and reads `GET /`.

Also `AGENTS.md`, `.cursor/rules`, and this `docs/` set.

### Why we built it

The rest of the project is easier to understand if the boxes exist before the machinery. It also stops a later session from inventing a second layout. The master prompt says the first session stops here on purpose.

### How it works

1. `dotnet test TaskFlow.slnx` builds every project and runs 5 tests.
2. `dotnet run --project src/TaskFlow.Api` binds `http://localhost:8080` (see `Properties/launchSettings.json`).
3. `GET /` returns `phase: 0` and `jobProcessing: false`.
4. Any job route 404s. There is no controller to hit.
5. `dotnet run --project src/TaskFlow.Worker` logs `JobProcessingEnabled=False` and waits on `Task.Delay` until Ctrl+C. Cancellation is caught so shutdown is a log line, not an error.

### Simple analogy

Phase 0 is an empty restaurant. The dining room (API), the order book (the database project), the pass (the queue project), and the kitchen (the worker) have signs on the doors. Nobody is cooking. The menu in `docs/` is the menu you intend to serve. It is not food.

### Important concepts learned

- A solution can encode architecture before it encodes features. The project references are the architecture.
- "The docs say so" is weaker than "the test fails if you add a forbidden reference".
- A process that starts is not a worker that consumes. The log says that, so a demo cannot accidentally claim otherwise.
- `const` is a compile-time value. Using it across assemblies can erase the reference you thought you had. See below.

### Important code locations

| File | What it does |
| --- | --- |
| `TaskFlow.slnx` | The solution. SDK 10 uses this XML format instead of a classic `.sln`. |
| `Directory.Build.props` | Shared target framework `net10.0`, nullable, warnings as errors. |
| `Directory.Packages.props` | Versions for the test and hosting packages. No RabbitMQ package. |
| `global.json` | SDK `10.0.401`, roll forward inside the 10.0 feature band. |
| `src/TaskFlow.Domain/DomainAssembly.cs` | Marker type. `Name` is `static readonly` so callers keep a real reference. |
| `src/TaskFlow.Application/ApplicationAssembly.cs` | Marker that touches `DomainAssembly`, which is why the reference exists. |
| `src/TaskFlow.Infrastructure/InfrastructureAssembly.cs` | Marker that touches both lower layers. |
| `src/TaskFlow.Api/Program.cs` | Builds the web app, logs the skeleton line, maps `GET /`, exposes `Program` to tests. |
| `src/TaskFlow.Worker/Worker.cs` | Hosted service that waits. No queue client. |
| `tests/TaskFlow.UnitTests/ProjectLayoutTests.cs` | Asserts who references whom. |
| `tests/TaskFlow.IntegrationTests/ApiSkeletonTests.cs` | Asserts the JSON body. |
| `scripts/verify.sh` | Build, then test. |

### Database impact

None. No connection string is read. `docs/DATABASE.md` is a design, not a migration.

### Messaging impact

None. No connection to a broker. Ack, redelivery, and prefetch are not configurable yet because there is no client.

### Failure scenarios

- The API process can fail to bind the port. That is an ordinary hosting failure. Nothing is queued.
- The worker can be cancelled. `ExecuteAsync` catches `OperationCanceledException` when the host is stopping and logs a clean stop.
- There is no broker outage to handle, and no database outage to handle.

### Why this design?

Empty projects with an enforced reference direction are cheaper to correct than a finished app in the wrong shape. A skeleton endpoint that says "not implemented" is safer than a sample `WeatherForecast` controller, which looks like a feature.

`static readonly` instead of `const`: the C# compiler copies a `const` into the caller and may then drop the unused assembly reference. The first version of the layout test failed with an empty reference list for that reason. A field read is a real use of the other assembly.

### Alternatives

- Generate the default web API template and leave `WeatherForecast` in place. Rejected: it looks like product behavior.
- Put all projects in one assembly until Phase 2. Rejected: the boundaries are the part worth learning early.
- Add EF Core and RabbitMQ packages now so they restore. Rejected: an unused client is how a fake implementation starts. Packages arrive with the feature.
- Write a classic `.sln` by hand. Rejected: `dotnet new sln` on this SDK emitted `TaskFlow.slnx`, and the CLI builds it.

### Tradeoffs

You can clone the repo and build it without Docker, which is good. You cannot show a job moving through a queue, which is the actual project. The design docs are ahead of the code. That is useful only if every page says so. If a later edit implements a feature and leaves "planned" in place, fix the doc in that same change.

### Interview questions

1. Why are the API and the worker different projects?
2. What does `GET /` prove, and what does it not prove?
3. Why is Domain not allowed to reference Infrastructure?
4. Why did a `const` string break a dependency test?
5. What is the next task, and why is it not "add RabbitMQ"?

### Interview answers

1. They are different hosts. The API will accept work. The worker will do work. Either one should be able to restart. They share Application and Infrastructure so the rules are not copied.
2. It proves the web host starts and that this build tells the truth about being a skeleton. It does not prove persistence, a queue, or a handler.
3. Domain is the job model. If it knows about EF Core or RabbitMQ, you cannot talk about the model without dragging the infrastructure into the conversation, and you cannot test transitions without a database.
4. `const` is inlined at compile time. After inlining, the caller may not reference the assembly at all. `static readonly` is read at runtime, so the reference stays. See `ProjectLayoutTests`.
5. The next task is the `Job` model and the legal transitions, tested without a database. RabbitMQ before a job exists has nothing honest to publish.

### Deep dive

#### Solution structure as an architectural test

People draw layers in a wiki and then reference whatever is convenient. `ProjectLayoutTests` reads `Assembly.GetReferencedAssemblies()` and compares the TaskFlow names. Application may name Domain. It may not name Infrastructure. The test failed until the marker types actually used the lower layer, which is the same rule you will follow later: a project reference you do not use is not a relationship the compiler keeps.

This test does not understand C# `InternalsVisibleTo` or reflection loaded later. It is a lock on the compile-time graph, which is the graph that matters for "Domain must not see RabbitMQ".

#### Why the worker waits on a delay

`BackgroundService.ExecuteAsync` is the .NET hook for "run until the host stops". `Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken)` is a wait, not a poll. There is no loop that logs every second, because a heartbeat that says "Worker running" would look like consumption. When the token fires, `Delay` throws `OperationCanceledException`. The filter `when (stoppingToken.IsCancellationRequested)` handles shutdown and lets any other cancellation error surface.

#### The partial `Program` class

Top-level statements generate a `Program` class that is not public. `WebApplicationFactory<Program>` lives in another assembly, so the test project could not name that class. `public partial class Program` in `Program.cs` makes the generated class public. That is a test seam, not a domain type.

### Things I should remember

- TaskFlow's queue is not built yet. `GET /` says so.
- SQL Server will remember the job. RabbitMQ will only carry the job id.
- Ack, retry, and redelivery are three different ideas. None of them run in this phase.
- Domain does not reference Infrastructure.
- The next code to write is the job and its legal status changes, with unit tests.
- Do not put this project on a resume until a later phase earns the bullet.

## Task: Phase 1 — Job, attempt, and the state machine

### What we built

An in-memory job. `Job.Create` starts it at `Pending`. The only way to change `Status` is a method on `Job`. Those methods consult `JobTransitions`. A forbidden pair throws `InvalidJobTransitionException` and leaves the object as it was.

`JobAttempt` is one try: a number, a worker id, a start, and later an outcome and a duration. The job keeps the list.

### Why we built it

The queue, the database, and the HTTP API will all ask the same question: "is this status change allowed?" If that answer lives in a controller or a SQL `UPDATE`, it will be copied wrong. One aggregate answers it before any of those exist.

### How it works

1. `Create` checks the type, the payload, and `maxAttempts >= 1`. A missing correlation id is generated. Status is `Pending`. Attempt count is 0.
2. `MarkQueued` moves `Pending` or `RetryScheduled` to `Queued` and clears `NextAttemptAt`.
3. `StartProcessing` is legal only from `Queued`. It increments `AttemptCount` and appends an open attempt.
4. `CompleteSuccessfully` closes that attempt and sets `Succeeded`.
5. `FailPermanently` closes it and sets `Failed`. No `NextAttemptAt`.
6. `RecordRetryableFailure` closes it as `RetryableFailure`. If `AttemptCount < MaxAttempts`, status becomes `RetryScheduled` and the caller-supplied `nextAttemptAt` is stored. If the budget is spent, status becomes `DeadLettered` and `NextAttemptAt` stays null.
7. `DeadLetter` is only for a job already in `RetryScheduled`. It does not add an attempt.
8. `RetryManually` moves `Failed` or `DeadLettered` back to `Queued`. The attempt count and the budget stay the same.

`MarkQueued` will not republish a failed job, and `RetryManually` will not publish a pending one. Both end at `Queued`, but they are different decisions.

### Simple analogy

The job is a claim ticket. Each attempt is a stamp on the back. The ticket can say "try again Tuesday" (`RetryScheduled`) or "we are done" (`Succeeded`, `Failed`, `DeadLettered`). You cannot stamp "done" onto a ticket that is still in the inbox, and you cannot scratch "done" off a successful ticket.

### Important concepts learned

- A job and an attempt are different objects. One job, many attempts, one open attempt at a time.
- Dead-lettering is a job status. The last attempt is still a `RetryableFailure`.
- The state machine does not know about RabbitMQ. Redelivery will later ask this object what to do. It does not do the asking yet.
- Time is an argument. The domain does not call `DateTime.UtcNow`, so the tests choose the clock.

### Important code locations

| File | What it does |
| --- | --- |
| `src/TaskFlow.Domain/Jobs/Job.cs` | Aggregate. Create and every status change. |
| `src/TaskFlow.Domain/Jobs/JobAttempt.cs` | One try. Only `Job` can complete it. |
| `src/TaskFlow.Domain/Jobs/JobTransitions.cs` | The allowed pairs. |
| `src/TaskFlow.Domain/Jobs/JobStatus.cs` | The seven statuses. |
| `src/TaskFlow.Domain/Jobs/JobAttemptOutcome.cs` | Succeeded, retryable failure, permanent failure. |
| `src/TaskFlow.Domain/Jobs/InvalidJobTransitionException.cs` | Names the `From` and `To` that were rejected. |
| `tests/TaskFlow.UnitTests/Jobs/JobLifecycleTests.cs` | Walks the real methods. |
| `tests/TaskFlow.UnitTests/Jobs/JobTransitionMapTests.cs` | Checks every enum pair against the legal list. |

### Database impact

None yet. The properties match the planned `Jobs` and `JobAttempts` columns in `docs/DATABASE.md`. There is no `DbContext` and no migration. Restarting the test process forgets the objects, which is what an in-memory model does.

### Messaging impact

None. Nothing is published. `NextAttemptAt` is a timestamp on the object, not a delayed RabbitMQ message.

### Failure scenarios

- Calling `StartProcessing` on a `Succeeded` job throws and does not add an attempt. That is the rule a later worker will use when a message is redelivered for a finished job. The worker is not written, so this is not yet a broker test.
- A retryable failure with no `nextAttemptAt`, or a time earlier than the failure, throws before the attempt is closed. The job stays `Processing`.
- A clock that moves backwards throws `ArgumentOutOfRangeException` and does not change status.
- Permanent failure ignores the remaining budget. One bad payload does not get four more tries.
- Spending the last attempt on a retryable error dead-letters. Passing a future `nextAttemptAt` does not save it.

### Why this design?

Methods on the aggregate beat a public `Status` setter. A setter would let a test, and later a controller, jump from `Pending` to `Succeeded`.

The backoff numbers are not in this type. Phase 4 owns the policy. The domain only insists that a scheduled retry has a time, and that an exhausted budget does not get one.

Manual retry does not refill `MaxAttempts`. That is a small, explicit choice. The alternative is silently giving the caller another full budget. Phase 4 can change it when the HTTP retry endpoint is real. Until then the tests document the stricter behavior.

### Alternatives

- Store status as a string and check it in the API. Rejected: the rule would be repeated in the worker.
- Put the transition table in Infrastructure next to EF. Rejected: Domain would not be testable without a database, and this phase is not allowed to take that dependency.
- Compute the 5s / 30s / 2m delays inside `RecordRetryableFailure`. Rejected: those numbers are configuration, and hard-coding them now hides the policy.
- Reset `AttemptCount` on manual retry. Rejected: the attempt rows would disagree with the counter. History stays. The budget stays.

### Tradeoffs

You can explain the lifecycle without Docker. You still cannot submit a job over HTTP or survive a restart. `Failed` and `DeadLettered` look terminal to a worker, but `RetryManually` can leave them. "Terminal" in an interview has to include that sentence.

A job at its budget that is manually queued will dead-letter on the next retryable failure. That may surprise you in the Phase 4 demo. It is written down here so it is not a surprise.

### Interview questions

1. What is the difference between a job and an attempt?
2. Which statuses can reach `Queued`, and why are there two methods?
3. Why is dead-letter not an attempt outcome?
4. What happens on the attempt that hits `MaxAttempts`?
5. Why doesn't this project call the database yet?

### Interview answers

1. The job is the work item: type, payload, status, budget. An attempt is one execution of it, with its own start, worker, outcome, and duration. `JobLifecycleTests` shows a job with two attempts and one final `Succeeded`.
2. `Pending` and `RetryScheduled` use `MarkQueued`, which is the publisher's move. `Failed` and `DeadLettered` use `RetryManually`, which is a human putting the job back. `Succeeded` cannot reach `Queued`.
3. The attempt records why that try ended: success, retryable, or permanent. Dead-letter is the job's decision after the retryable tries run out. The last attempt stays `RetryableFailure`.
4. `RecordRetryableFailure` sees `AttemptCount >= MaxAttempts`, sets `DeadLettered`, and stores no `NextAttemptAt`. A `maxAttempts` of 1 does this on the first retryable failure.
5. The rules are easier to test without SQL Server, and a wrong rule is cheaper to fix before the migration exists. Phase 2 is the persistence step. This object is not a database.

### Deep dive

#### Why the transition map and the methods both exist

`JobTransitions` answers "is this pair legal?" `Job` answers "who is allowed to ask?" `Failed -> Queued` is legal, and `Pending -> Queued` is legal, but they are not the same operation. `MarkQueued` refuses a failed job. `RetryManually` refuses a pending job. The map test iterates every enum pair so a new status cannot silently become reachable. The lifecycle tests call the methods, so a map that says yes while a method forgets to check still fails.

#### Clocks and partial updates

Every method validates, then changes fields. `RecordRetryableFailure` checks the next time before it completes the attempt. If the time is missing, the attempt stays open and the status stays `Processing`. That matters later: a worker that fails validation must not ack a message for a job it left half-updated. There is no message yet. The habit starts here.

`EnsureClock` rejects a timestamp older than `UpdatedAt`. Tests pass the time in. Production code will pass `TimeProvider` or `DateTimeOffset.UtcNow` from the application layer, not from inside `Job`.

### Things I should remember

- Status changes go through `Job`. There is no public setter.
- One job, many attempts. Dead-letter is a job status.
- The retry delay is chosen by the caller. This phase does not implement backoff.
- Manual retry does not refill the attempt budget.
- None of this is saved. The next task is EF Core and the three job HTTP endpoints, still without RabbitMQ.
- `GET /` still says job processing is off. That is still true.

## Task: Phase 2 — Store the job in SQL Server

### What we built

`POST /api/v1/jobs` validates the body and inserts a `Job` row. `GET /api/v1/jobs/{id}` reads that row and its attempts. `GET /api/v1/jobs` lists rows, newest first, with page, page size, status, type, and a created-time window. Swagger is at `/swagger`. Errors use Problem Details.

A new job is `Pending`. It is not `Queued`. RabbitMQ is not called.

If `ConnectionStrings:TaskFlow` is missing, those routes return 503 and `GET /` still works.

### Why we built it

The state machine needed a system of record before a queue. The HTTP call should return an id for a row that survives a process restart. Calling the status `Queued` before a broker exists would be a lie the later worker would trust.

### How it works

1. The API reads `type`, `payload`, optional `maxAttempts`, and optional `correlationId`.
2. `JobSubmissionService` rejects an unknown type, a payload that is not a JSON object, and an attempt budget outside 1–20. The default budget is 5.
3. `Job.Create` builds a `Pending` job. The clock comes from `TimeProvider`, not from inside the entity.
4. `JobRepository` adds it and calls `SaveChanges`. That is one SQL transaction. There is no second step that publishes.
5. The response is 201, a `Location` header, and the stored job. `status` is `Pending`.
6. List and get run through `JobQueryService`. Get includes attempts ordered by number. List does not load attempts.

`dotnet ef database update` applies migration `InitialJobSchema`. Tests apply that same migration to a SQL Server 2022 container.

### Simple analogy

The API writes the order into the book and hands you the ticket number. It does not put a slip on the kitchen rail. The kitchen (RabbitMQ and the worker) is not open yet, so the ticket says "written down", not "being cooked".

### Important concepts learned

- The database row is the job. The HTTP response is a copy of that row at one moment.
- `Pending` and `Queued` are different. `Queued` means a publisher has handed the id to the broker. That publisher does not exist yet.
- EF Core maps the domain types. The domain project still does not reference EF.
- Integration tests can use a real SQL Server without you installing one, by starting a container. That is Testcontainers.

### Important code locations

| File | What it does |
| --- | --- |
| `src/TaskFlow.Application/Jobs/JobSubmissionService.cs` | Validates and creates the `Pending` job. |
| `src/TaskFlow.Application/Jobs/JobQueryService.cs` | Checks page and date bounds, then queries. |
| `src/TaskFlow.Application/Jobs/KnownJobTypes.cs` | The allow-list until handlers exist. |
| `src/TaskFlow.Infrastructure/Persistence/TaskFlowDbContext.cs` | The EF model. |
| `src/TaskFlow.Infrastructure/Persistence/JobConfiguration.cs` | Table, checks, and the three list indexes. |
| `src/TaskFlow.Infrastructure/Persistence/JobRepository.cs` | Insert, get, and list. SQL failures become `JobDatabaseUnavailableException`. |
| `src/TaskFlow.Infrastructure/Persistence/Migrations/20261003120503_InitialJobSchema.cs` | The migration that was applied in tests. |
| `src/TaskFlow.Api/Jobs/JobEndpoints.cs` | The three routes. No payload is logged. |
| `tests/TaskFlow.IntegrationTests/JobApiTests.cs` | Create, filter, page, and not-found against SQL Server. |
| `tests/TaskFlow.IntegrationTests/JobPersistenceTests.cs` | A succeeded attempt survives a new `DbContext`. |

### Database impact

Tables `Jobs` and `JobAttempts`. Status and attempt outcome are ints, matching the enums. `Duration` is ticks in a `bigint`. Checks: `MaxAttempts >= 1`, status 0–6, outcome null or 0–2. Unique `(JobId, AttemptNumber)`. Cascade delete from job to attempts. Indexes for the list query only.

Creating a job is one `SaveChanges`. Publishing is not in that transaction, because publishing does not happen.

### Messaging impact

None. A crash after the insert loses nothing that was promised, because the API never promised a queue message. The later publish gap is still ahead of us.

### Failure scenarios

- Unknown type or a JSON array payload: 400, no row.
- SQL Server not configured: 503, no row.
- SQL Server down after configuration: 503. The repository does not report success.
- Unknown id: 404.
- Page size above 100, or `createdFrom` after `createdTo`: 400, no query.
- Process restart: the row is still there. The in-memory object from Phase 1 was not.

### Why this design?

The repository is the only place that knows EF. Application code asks for a `Job` and gets a `Job`. That kept the state-machine tests free of SQL, and let the new tests prove the mapping separately.

`Pending` is the honest initial status. The contract already said not to claim `Queued` when the publish did not happen.

Command logging for EF is `Warning` in `appsettings.json`. Information-level command logs include parameter values, and one of those values is the payload.

### Alternatives

- Store the status only after a fake in-memory queue accepts it. Rejected: that would be a queue that is not RabbitMQ.
- Use EF InMemory for the integration tests. Rejected: it does not run this migration or these indexes.
- Auto-migrate on every API startup. Rejected: `dotnet run` would then require SQL Server just to serve `GET /`. Migration is an explicit `dotnet ef database update`, and the tests call `Migrate` on their container.
- Mark the job `Queued` anyway, to match the happy-path diagram. Rejected: the diagram includes a publish. This phase does not.

### Tradeoffs

You can restart the API and still fetch the job. You cannot show a worker picking it up. The allow-list of types is a stand-in for the handler registry; a type that is accepted today has no handler. Phase 3 has to keep those names in sync.

Testcontainers makes the SQL tests slower and requires Docker. The unit tests still run without it. The 503 behavior is tested without a container.

### Interview questions

1. Why is a newly created job `Pending` instead of `Queued`?
2. What is saved in SQL Server, and what is deliberately not sent anywhere?
3. Why is the payload absent from the log line?
4. What does 503 mean here, and what does 400 mean?
5. How do you know the attempt mapping works if the API never creates an attempt?

### Interview answers

1. `Queued` means the broker has been asked to deliver the job id. This phase only commits the row. `JobApiTests` asserts the response status is `Pending`.
2. The job row holds type, payload, status, attempt budget, timestamps, and correlation id. Attempts are a child table. No message is published.
3. The payload can be large and may contain user data. The log line has job id, type, status, and correlation id. EF command logging is set to Warning so parameter values are not written at the default level.
4. 400 is a bad request: unknown type, bad payload, bad page. 503 means the database is missing or unreachable, and the call did not pretend to store the job.
5. `JobPersistenceTests` builds a succeeded job with `Job.StartProcessing` and `CompleteSuccessfully`, saves it, then loads it with a new context. The attempt number, worker, outcome, and two-second duration come back.

### Deep dive

#### EF and a constructor that has no setters

`Job.Id`, `Type`, `Payload`, `MaxAttempts`, `CreatedAt`, and `CorrelationId` have no setters. EF will not map a get-only property it has not been told about, and it can only fill those values through the constructor. The first migration attempt failed on `maxAttempts` until `JobConfiguration` mapped `MaxAttempts` explicitly. The private constructor parameter names match the properties, so EF calls that constructor and then uses private setters for `Status` and the timestamps.

The attempt list is the field `_attempts`. The configuration points the `Attempts` navigation at that field. That is why a reload can show attempts even though nothing outside `Job` can add one.

#### One transaction, and the gap we have not reached

`SaveChanges` commits the insert or rolls it back. There is no second resource in the transaction. When Phase 3 publishes after that commit, a crash in between will leave a `Pending` row and no message. The row is still the truth. The API must not change it to `Queued` unless the publish returned success. That rule is why this phase stops at `Pending`.

### Things I should remember

- `POST /api/v1/jobs` stores a `Pending` job. It does not queue one.
- 503 means the database was not used. 400 means the body was rejected.
- The payload is in the row and not in the log.
- Docker is required for the SQL Server tests, not for the unit tests.
- The next task is a real RabbitMQ publish, and only then `MarkQueued`.

## Task: RabbitMQ topology and publisher

### What we built

`POST /api/v1/jobs` now hands the job to RabbitMQ. The API stores the row as `Pending`, publishes a small message, waits for the broker to confirm it, and only then marks the row `Queued`. The API declares the exchange `taskflow.jobs`, the queue `taskflow.jobs.process`, and the binding `job.process` the first time it connects.

Nothing consumes the queue. Messages wait there until the worker task lands.

### Why we built it

Phase 2 stopped at `Pending` because `Queued` would have been a lie without a broker. This task makes `Queued` true: the status now means "RabbitMQ accepted and routed a message for this job id".

### How it works

1. `JobEndpoints.Submit` checks `TaskFlowInfrastructureStatus`. No SQL Server or no RabbitMQ setting means 503, and nothing is stored.
2. `JobSubmissionService.SubmitAsync` validates and calls `IJobRepository.AddAsync`. The row commits as `Pending`.
3. It calls `IJobPublisher.PublishAsync(new JobMessage(job.Id, job.Type, job.CorrelationId))`.
4. `RabbitMqJobPublisher` gets the shared connection from `RabbitMqConnectionProvider`. The first call opens the connection and runs `RabbitMqTopology.DeclareAsync`.
5. The publisher opens a channel with publisher confirmations and tracking on, and calls `BasicPublishAsync` with `mandatory: true`, `Persistent = true`, `MessageId` = job id, and the correlation id. With tracking on, that call does not finish until the broker acks. It throws if the broker nacks or returns the message as unroutable.
6. After the confirm, the service calls `job.MarkQueued(now)` and `IJobRepository.SaveChangesAsync`.
7. The API returns 201 with `status: "Queued"`.

If step 5 fails or takes longer than `TaskFlow:RabbitMq:PublishTimeout` (5 seconds), the publisher throws `JobPublishFailedException`. The service wraps it in `JobNotQueuedException(jobId)` and does not touch the status. The API returns 503 with `jobId` and `jobStatus: "Pending"`.

If step 6 fails, the service throws `JobQueuedStateNotSavedException(jobId)`. The API returns 503 with the same two fields and a detail that says the message was published.

### Simple analogy

The restaurant now writes the order in the book (SQL Server), then clips a slip with the ticket number onto the kitchen rail (RabbitMQ). The host waits for the rail to click shut (the confirm) before telling you "your order is in". If the rail is jammed, the host says "it's written down, but it's not on the rail", and gives you the ticket number. The kitchen staff (the worker) has not been hired yet, so the slips just hang there.

### Important concepts learned

- `BasicPublishAsync` returning is not the same as the broker having the message. Publisher confirms make the broker say "I have it".
- `mandatory: true` turns "no queue was bound to this routing key" into an error. Without it, RabbitMQ silently drops an unroutable message.
- A durable queue plus a persistent message is what survives a broker restart. Either one alone is not enough.
- Storing first and publishing second means the possible mismatch is "row without message", which the row can explain. Publishing first would allow "message without row", which a worker cannot explain.
- The Application project still has no RabbitMQ types. It knows `IJobPublisher` and `JobMessage`.

### Important code locations

| File | What it does |
| --- | --- |
| `src/TaskFlow.Application/Jobs/JobSubmissionService.cs` | Store, publish, mark `Queued`, save. Maps the two failure windows to two exceptions. |
| `src/TaskFlow.Application/Jobs/IJobPublisher.cs` | The port. Returns only after a confirm. |
| `src/TaskFlow.Application/Jobs/JobMessage.cs` | The message body. No payload. |
| `src/TaskFlow.Application/Jobs/JobNotQueuedException.cs` | Stored, not published. Carries the job id. |
| `src/TaskFlow.Application/Jobs/JobQueuedStateNotSavedException.cs` | Published, `Queued` not saved. Carries the job id. |
| `src/TaskFlow.Infrastructure/Messaging/RabbitMqTopology.cs` | Names and the idempotent declare. |
| `src/TaskFlow.Infrastructure/Messaging/RabbitMqConnectionProvider.cs` | One lazy connection with automatic recovery. Declares the topology before it hands the connection out. |
| `src/TaskFlow.Infrastructure/Messaging/RabbitMqJobPublisher.cs` | Confirmed, mandatory, persistent publish. Maps client errors and timeouts to `JobPublishFailedException`. |
| `src/TaskFlow.Infrastructure/DependencyInjection.cs` | Registers messaging only when `ConnectionStrings:RabbitMq` is a valid `amqp://` URI. Returns `TaskFlowInfrastructureStatus`. |
| `src/TaskFlow.Api/Jobs/JobEndpoints.cs` | The 503 shapes with `jobId` and `jobStatus`. |
| `tests/TaskFlow.UnitTests/Jobs/JobSubmissionServiceTests.cs` | Order of operations and the failure branches, with a recording publisher. |
| `tests/TaskFlow.IntegrationTests/JobPublishingTests.cs` | Real RabbitMQ: message shape, topology, unreachable broker, missing setting. |
| `tests/TaskFlow.IntegrationTests/RabbitMqFixture.cs` | The `rabbitmq:4.1` container. |

### Database impact

No migration. Creating a job is now two `SaveChanges` calls: the insert, and the `Queued` update after the confirm. They are separate transactions on purpose, because the publish sits between them.

### Messaging impact

The API is a producer. It declares the topology, so a fresh broker works without manual setup. The message is about 100 bytes of JSON. The worker still consumes nothing, so the queue only grows.

### Failure scenarios

- RabbitMQ not configured: 503 `RabbitMQ is not configured.`, no row. Test: `Missing_rabbitmq_setting_returns_503_without_storing_a_job`.
- RabbitMQ unreachable or slow: 503 `RabbitMQ is unavailable.` with `jobId`. The row stays `Pending`. Test: `Unreachable_broker_returns_503_and_leaves_the_stored_job_pending`.
- The broker nacks, or returns the message as unroutable: the same 503. This is handled in `RabbitMqJobPublisher` but no test forces it.
- SQL Server fails after the confirm: 503 `SQL Server is unavailable.` with `jobId`. The message is in the queue and the row says `Pending`. Unit test only: `Submit_reports_a_published_job_whose_queued_status_was_not_saved`.
- The confirm is lost when the connection drops after the broker accepted the message: reported as a failure, but a message may exist. A later consumer may see it.
- The process dies between any two steps: no response, and one of the two windows above is left behind.

### Why this design?

Confirms are the only way the API can honestly say `Queued`. Without them the status would mean "we tried".

The connection is lazy so the API can start, list, and get jobs while RabbitMQ is down. A missing setting is rejected up front, because a job stored with no way to publish it would never move.

A channel per publish keeps the code simple. An `IChannel` should not be shared by concurrent publishers, and a pool is more code than this task needs.

### Alternatives

- Publish before the insert. Rejected: a worker could receive an id that has no row.
- Fire-and-forget publish. Rejected: `Queued` would include messages the broker never accepted.
- Retry the publish inside the request. Rejected for now: it hides the failure and makes the request slow. A republisher or an outbox is the honest fix.
- Return 201 `Pending` when RabbitMQ is not configured. Rejected: that job would never be published by anything.
- Put the status in a Problem Details `status` extension. Rejected: `status` is already the HTTP code in RFC 9457, so the field is `jobStatus`.

### Tradeoffs

Every submit now costs a SQL insert, a broker round-trip for the confirm, a channel open, and a SQL update. That is slower than Phase 2 and much easier to explain. A failed publish leaves an orphan `Pending` row that nothing picks up yet. The client gets the id, but retrying the HTTP call creates a second job.

### Interview questions

1. When does a TaskFlow job become `Queued`, exactly?
2. What do publisher confirms give you that `BasicPublish` alone does not?
3. Why `mandatory: true`?
4. Why insert first and publish second?
5. What does the client see if RabbitMQ is down?
6. What is left behind if SQL Server fails right after the confirm?
7. Why is the payload not in the message?

### Interview answers

1. After the broker confirms the publish and the second `SaveChanges` commits. `JobSubmissionService` calls `MarkQueued` only after `PublishAsync` returns, and `JobSubmissionServiceTests` checks that a failed publish leaves `Pending`.
2. A confirm is the broker's ack for that message. Without it, the client library may have buffered bytes that never arrived. `RabbitMqJobPublisher` opens its channel with confirmation tracking, so `BasicPublishAsync` waits for the ack and throws on a nack.
3. If no queue is bound to `job.process`, RabbitMQ drops the message unless `mandatory` is set. With it, the message comes back and the publisher treats it as a failure instead of marking the job `Queued`.
4. A row without a message can be found and explained later. A message without a row is an id the worker cannot load. The API commits `Pending` first for that reason.
5. 503 Problem Details titled "RabbitMQ is unavailable." with `jobId` and `jobStatus: "Pending"`. `GET` of that id returns `Pending`. `JobPublishingTests` checks both.
6. A message in `taskflow.jobs.process` for a row that says `Pending`. The API returns 503 with the `jobId`. The consumer task has to handle that delivery.
7. The row is the system of record and the payload can hold user data. The message carries `jobId`, `type`, and `correlationId`, and the integration test checks that a payload value is not in the body.

### Deep dive

#### What "confirmed" means in RabbitMQ.Client 7

In 7.x, `CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true)` makes the client track each delivery tag. `BasicPublishAsync` then completes only when the broker sends `basic.ack` for that tag. A `basic.nack` throws `PublishException`. A `basic.return`, which happens with `mandatory: true` and no matching queue, throws a `PublishException` with `IsReturn` set. `RabbitMqJobPublisher` maps both to `JobPublishFailedException`.

The linked `CancellationTokenSource` adds the timeout. A timeout cancels the wait, not the broker's work. If the broker accepted the message just before the timeout, the API reports a failure for a message that exists. That is the "lost confirm" case, and it is why the consumer has to be idempotent anyway.

#### Why the topology is declared by the producer

Declares are idempotent when the arguments match. The API declares before its first publish so a fresh container works. `First_publish_declares_the_exchange_queue_and_binding` deletes both, submits a job, and then checks them with passive declares. When the worker exists, it will declare the same topology, so either process can start first.

### Things I should remember

- `Queued` means the broker confirmed. It does not mean a worker has seen it.
- Store, publish, mark. Two SQL transactions with a publish between them.
- 503 with `jobId` means the row exists and is `Pending`.
- `status` in Problem Details is the HTTP code. The job's state is `jobStatus`.
- The next task is the consumer, with manual ack, and it must cope with a delivery for a `Pending` row.

## Task: Worker consumer and job handlers

### What we built

`TaskFlow.Worker` now does work. It consumes `taskflow.jobs.process` with manual acknowledgement and prefetch 1. For each message, it loads the job from SQL Server, claims it, runs the handler for the job type, saves `Succeeded` or `Failed`, and only then acks.

Handlers are small classes behind `IJobHandler`: `demo.success`, `demo.permanent-failure`, `demo.slow`, `email.send` (simulated), `report.generate`, and `data.process`. `JobHandlerRegistry` is the one list of types. The API rejects any type that is not in it, so `demo.transient-failure` returns 400 until Phase 4 adds retries.

### Why we built it

After the publisher task, jobs piled up in the queue as `Queued`. This task is the other half of "asynchronous": a separate process that picks the work up and records what happened.

### How it works

1. `Program.cs` in `TaskFlow.Worker` calls `AddTaskFlowApplication`, `AddTaskFlowInfrastructure`, and `AddTaskFlowJobConsumer`. The last one throws at startup if either connection string is missing.
2. `RabbitMqJobConsumer.ExecuteAsync` connects (retrying every `ConnectRetryDelay` while RabbitMQ is down), opens a channel, calls `BasicQosAsync(prefetch: 1)`, and starts `BasicConsumeAsync(autoAck: false)`.
3. For each delivery, it parses `{ jobId, type, correlationId }`. If that fails, it calls `BasicRejectAsync(requeue: false)`.
4. It opens a DI scope and calls `JobProcessor.ProcessAsync(jobId, workerId)`:
   - not found: return `SkippedNotFound`
   - `Pending`: call `MarkQueued`. The message proves the publish happened.
   - not `Queued`: return `SkippedNotQueued` without calling the handler
   - `Queued`: `StartProcessing` and save (the claim, with a new attempt row), run the handler, `CompleteSuccessfully` or `FailPermanently`, save
5. Every return value means "the outcome is saved", so the consumer calls `BasicAckAsync`.
6. If SQL Server is unavailable, the consumer waits `DatabaseRetryDelay` and calls `BasicNackAsync(requeue: true)`.
7. On shutdown, it cancels the consumer and waits for the in-flight message to finish and ack.

### Simple analogy

The kitchen is open. A cook takes one slip off the rail at a time (prefetch 1), checks the order book to see if that order is still waiting, writes "cooking, by cook 3" in the book (the claim), cooks, writes "served" or "couldn't make it", and only then throws the slip away (the ack). If the book already says "served", the cook throws the slip away without cooking again.

### Important concepts learned

- The ack is the last step, after the outcome save. Ack means "RabbitMQ can forget this message", not "the job succeeded". A `Failed` job is acked too.
- Prefetch is how many unacked messages RabbitMQ will push to one consumer. With 1, a slow job does not hold other jobs hostage on this worker.
- The row decides what a delivery means. The same message can be the first delivery, a redelivery, or a duplicate. Only a `Queued` row runs.
- `ack`, `nack(requeue: true)`, and `reject(requeue: false)` are three different decisions: done, try later, and throw away.
- A handler knows only its `JobContext`. It does not know about RabbitMQ, EF Core, or the ack.

### Important code locations

| File | What it does |
| --- | --- |
| `src/TaskFlow.Application/Jobs/JobProcessor.cs` | Skip rules, Pending promotion, claim, handler run, outcome save |
| `src/TaskFlow.Application/Jobs/Handlers/IJobHandler.cs` | `IJobHandler`, `JobContext`, `JobHandlerResult` |
| `src/TaskFlow.Application/Jobs/Handlers/JobHandlerRegistry.cs` | The single list of job types, used by the API and the worker |
| `src/TaskFlow.Application/Jobs/Handlers/DemoHandlers.cs` | `demo.success`, `demo.permanent-failure`, `demo.slow` |
| `src/TaskFlow.Application/Jobs/Handlers/SimulatedHandlers.cs` | `email.send`, `report.generate`, `data.process` |
| `src/TaskFlow.Infrastructure/Messaging/RabbitMqJobConsumer.cs` | Prefetch, manual ack, reject, delayed nack, graceful stop |
| `src/TaskFlow.Infrastructure/Messaging/WorkerOptions.cs` | `PrefetchCount`, `WorkerId`, `DatabaseRetryDelay`, `ConnectRetryDelay` |
| `src/TaskFlow.Infrastructure/DependencyInjection.cs` | `AddTaskFlowJobConsumer` |
| `src/TaskFlow.Infrastructure/Persistence/Migrations/20261003150720_JobClientGeneratedIds.cs` | Snapshot-only migration for client-generated ids |
| `src/TaskFlow.Worker/Program.cs` | The worker composition root |
| `tests/TaskFlow.UnitTests/Jobs/JobProcessorTests.cs` | Every processor branch and the handler payload checks |
| `tests/TaskFlow.IntegrationTests/WorkerTests.cs` | The real consumer against real SQL Server and RabbitMQ |

### Database impact

Two saves per run: the claim (`Processing` plus a new `JobAttempts` row) and the outcome. Migration `JobClientGeneratedIds` has empty `Up` and `Down`. It only records in the model that `Jobs.Id` and `JobAttempts.Id` are set by the code, not the database.

### Messaging impact

The worker is the first consumer. RabbitMQ now deletes a message only after the worker acks it. An unacked message goes back to the queue if the worker's channel closes.

### Failure scenarios

- Duplicate message for a finished job: acked, no second attempt. Test: `A_duplicate_message_for_a_succeeded_job_is_acked_without_a_second_attempt`.
- Message for a `Pending` row (the publish-then-save window): promoted and run. Test: `A_message_for_a_pending_row_promotes_and_runs_the_job`.
- Unreadable body: rejected, dropped, the queue keeps moving. Test: `An_unreadable_message_is_rejected_and_does_not_block_the_queue`.
- Unknown type in a stored row: `Failed` with `NoHandlerRegistered`. Unit test only.
- Handler throws: `Failed` with the exception type name. Unit test only.
- SQL Server down: delayed nack with requeue. Implemented, not covered by a test that stops SQL Server.
- Worker crash after the claim: the row stays `Processing` and the redelivery is skipped. The job is stuck. **Known gap, Phase 5.**
- Two workers loading the same `Queued` row at the same moment: both could claim it, because the claim is a plain save. **Known gap, Phase 5.**

### Why this design?

Deciding from the row means a duplicate message cannot run a finished job again. Acking after the save means a crash before the save gives the job back to RabbitMQ instead of losing it.

Promoting `Pending` is safe because the API inserts the row before it publishes. A message cannot exist for a row that was never stored.

Treating handler exceptions as permanent is a deliberate placeholder. Without a retry policy, the other choices are a requeue loop or a stuck message.

The database-outage nack waits first, so a SQL outage costs one requeue per message every 5 seconds instead of a hot loop.

### Alternatives

- `autoAck: true`. Rejected: a crash mid-handler would lose the job.
- Ack at the start of the handler. Rejected: same loss, just later.
- Requeue a failed handler with `BasicNack(requeue: true)`. Rejected: this is the infinite retry loop the rules forbid. Phase 4 schedules retries with `NextAttemptAt`.
- Put the handlers in Infrastructure. Rejected: they are business steps, and the API also needs the type list.
- Higher prefetch with parallel handlers. Rejected for now: one message at a time keeps one obvious ack site. Scaling is by process, which is the next task.

### Tradeoffs

The worker is simple and honest about what it has done, but it cannot retry. A crash at the wrong moment strands a job in `Processing`. The integration tests prove one worker. They do not prove two workers or a real crash.

### Interview questions

1. When does TaskFlow ack a message, and why then?
2. What happens when the same message arrives twice?
3. What does prefetch 1 do?
4. Why does a `Pending` row get processed instead of skipped?
5. What is the difference between `nack(requeue: true)` and `reject(requeue: false)` here?
6. What happens if the worker crashes after it claims a job?
7. Why did the EF model need `ValueGeneratedNever`?

### Interview answers

1. After `JobProcessor` returns, which is after the outcome save. `RabbitMqJobConsumer` has one ack site, right after `ProcessAsync`. A crash before that save leaves the message unacked, so RabbitMQ delivers it again.
2. The second delivery loads the row, sees `Succeeded`, and is acked without running the handler. `WorkerTests` republishes a message for a finished job and checks there is still one attempt.
3. RabbitMQ pushes at most one unacked message to this worker. The next job waits in the queue, where another worker could take it.
4. The API inserts before it publishes, so a message means the publish happened. A `Pending` row with a message is the "published, but the `Queued` save failed" window. `JobProcessor` calls `MarkQueued` and runs it. `WorkerTests` proves that with a row inserted as `Pending`.
5. The nack is for a SQL outage: put the message back after a delay, because the job is fine and the database is not. The reject is for a body that is not a job message: throw it away, because no amount of retrying will parse it.
6. The row stays `Processing`. The redelivery sees a status that is not `Queued` and skips it, so the job is stuck. That is a known gap, and Phase 5 handles stuck claims. Do not claim it is solved.
7. EF assumes a Guid key is generated by the store. A new attempt with its id already set, added to a tracked job, looked like an existing row, and EF would have updated it instead of inserting it. Marking the ids as never generated makes EF insert the new attempt. The `WorkerTests` success test reads the attempt back.

### Deep dive

#### The EF key pitfall

Phase 2 saved jobs with `Add`, which marks the whole graph as new, so the key setting never mattered. The worker does something different. It loads a tracked `Job`, and `StartProcessing` appends a `JobAttempt` to the private `_attempts` list. On `SaveChanges`, EF discovers that attempt through the navigation. With a store-generated key, EF treats an entity with a non-default key as one it should already know about, and marks it `Modified`. The SQL would be an `UPDATE` that matches zero rows, and EF would throw a concurrency exception. `ValueGeneratedNever` tells EF the code owns the id, so a newly discovered entity is `Added`. The column type did not change, which is why the migration's `Up` is empty. EF 10 still needs that migration, because it refuses to migrate when the model and the snapshot disagree.

#### Graceful shutdown

`JobProcessor` runs with `CancellationToken.None`, not the host's stopping token. On shutdown, the consumer cancels its subscription, waits for the in-flight message to be saved and acked, and then closes the channel. `demo.slow` is capped at 30 seconds, which is the default host shutdown timeout. A longer handler would be cut off by the host and its message would be redelivered.

### Things I should remember

- Ack after the outcome save. One ack site.
- The row decides. Only `Queued` or `Pending` runs.
- `Failed` jobs are acked too. Ack is not success.
- Handler exceptions are permanent until Phase 4.
- A crash after the claim strands the job. Phase 5.
- The next task is two worker processes on one queue.
