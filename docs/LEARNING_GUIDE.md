# TaskFlow learning guide

This is the textbook for the repository. If a sentence describes behavior, the code must do that behavior. Sections marked **Planned** are the design. They are not features you can demo.

## What is TaskFlow?

**Status: the job model exists in memory. The API, database, and queue do not.**

TaskFlow will be a small background-job system. A client will ask an API to do something that should not block the HTTP call, such as "send this email" or "generate this report". The API will remember the job. A worker process, running separately, will do it later.

You are building it to learn two ideas well enough to explain in an interview:

1. Asynchronous processing with a message queue.
2. Reliable processing: acknowledgements, retries, idempotency, and what happens when a worker dies.

It is not a SaaS product. Swagger will be the UI. There is no React app.

Today the API starts and `GET /` says job processing is off. The worker starts and waits. No job can be submitted. You can create a `Job` in a unit test and walk it from `Pending` to `Succeeded`, `Failed`, or `DeadLettered`. Nothing saves that object.

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

## What RabbitMQ will eventually do (planned)

RabbitMQ will be the transport, not the database.

Planned topology:

- Exchange `taskflow.jobs` (durable, direct)
- Queue `taskflow.jobs.process` (durable)
- Routing key `job.process`
- Message body: job id and small metadata, not the full payload
- Manual acknowledgement

The worker will pull a message, load the job from SQL Server, do the work, save the outcome, and only then acknowledge. If the worker dies first, RabbitMQ delivers the message again. That second delivery is redelivery, not a new job.

The .NET client is not referenced yet. On 2026-10-03 the newest stable `RabbitMQ.Client` on NuGet was 7.2.2. Phase 3 re-checks [the official .NET client guide](https://www.rabbitmq.com/docs/dotnet) before locking the version.

## What you will learn

| Phase | You will be able to point at code and say |
| --- | --- |
| 0 (done) | How the solution is cut into layers, and why the docs are part of the project |
| 1 (done) | What a job is, what an attempt is, and which status changes are legal |
| 2 | How an HTTP request becomes a row |
| 3 | How a message is published, consumed, and acknowledged |
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
| Background jobs | Implemented in memory | `Job`, `JobAttempt`. Not stored or queued yet |
| Job state machine | Implemented | `JobTransitions`, `JobLifecycleTests` |
| Message queues | Designed | ADR-002 |
| RabbitMQ | Designed | ADR-002, `ARCHITECTURE.md` |
| Producer | Designed | The API, later |
| Consumer | Designed | The worker, later. Today's worker consumes nothing |
| Competing consumers | Designed | Two workers, one queue |
| Acknowledgement | Designed | ADR-003 |
| Redelivery | Designed | ADR-003, ADR-004 |
| Retries | Designed | ADR-005 |
| Exponential backoff | Designed | ADR-005 |
| Jitter | Designed | ADR-005 |
| Dead lettering | Designed | Status `DeadLettered`, not a second RabbitMQ queue in v1 |
| Idempotency | Designed | ADR-004 |
| At-least-once processing | Designed | ADR-004 |
| Eventual consistency | Designed | The gap between SQL commit and publish |
| Transactions | Designed | `DATABASE.md` |
| Database concurrency | Designed | Compare-and-update claim |
| Optimistic concurrency | Designed | Status predicate on update |
| Strategy / handler pattern | Designed | `IJobHandler`, Phase 3 |
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

A job can be `Queued` in SQL Server while the publish is still in flight, or `Queued` in SQL Server with no message at all if the publish failed. Those are different bugs. The outbox is the later fix for the second one. It is not built.

## Failure scenarios I understand

None of these can be executed. The "how TaskFlow behaves" line is the planned behavior. Do not answer an interview with these as if you have shipped them.

### 1. Worker crashes before ack

- What happened: the worker took a message and died before ack. **Planned scenario.**
- Why: the process is gone; the broker never heard "done".
- Planned behavior: RabbitMQ redelivers to a live consumer.
- Tradeoff: the work may run twice. The alternative, auto-ack, loses the work.
- Interview question: Why is manual ack worth the duplicate?

### 2. RabbitMQ redelivers an unacknowledged message

- Planned behavior: the consumer loads the job by id and decides from the row, not from memory.
- Tradeoff: every consumer must handle a message it has never seen and a message it might have seen.
- Interview question: What do you store in the message if the row is the source of truth?

### 3. Job succeeds but the worker crashes before ack

- Planned behavior: the row is `Succeeded`. The next worker sees a terminal status, acks, and does not run the handler.
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

- Planned behavior: status `Failed`, attempt row records a permanent outcome, message acked.
- Tradeoff: a bug in classification can drop a job that should have been retried. Classification has to be tested.
- Interview question: Give an example of each class in this project.

### 7. Maximum attempts are reached

- Planned behavior: `DeadLettered`, not another delay.
- Tradeoff: a human can still call the manual retry endpoint. The queue will not spin by itself.
- Interview question: Is dead-letter a RabbitMQ queue or a status? In this design it is a status. Say that clearly.

### 8. RabbitMQ is unavailable

- Planned behavior: the API cannot publish. The request fails. If the row was already committed, the gap from ADR-007 is visible.
- Tradeoff: the client can retry the HTTP call. The submit path has to be careful not to create two jobs for one user action. Idempotency keys are not in v1; `correlationId` is for tracing, not deduplication, unless a later task says otherwise.
- Interview question: What does the client see?

### 9. SQL Server is unavailable

- Planned behavior: submit fails and nothing is published. A worker that cannot load or claim the job does not ack, so the message returns, unless the error is clearly permanent.
- Tradeoff: not acking when the database is down is correct and can also pile up unacked messages. Prefetch stays small for that reason.
- Interview question: Why is "nack and requeue immediately" still a bad default while SQL is down?

### 10. API stores state but publish fails

- Planned behavior: the row remains, the response is an error, and the docs admit the job will not run until something republishes it.
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
