# Demo

## What you can show today

1. `dotnet test TaskFlow.slnx` — 5 tests, all passing.
2. `dotnet run --project src/TaskFlow.Api` and open `http://localhost:8080/`. The JSON says phase 0 and `jobProcessing: false`.
3. `dotnet run --project src/TaskFlow.Worker`. The log says the worker skeleton started and job processing is disabled. Stop it with Ctrl+C. It logs that it is stopping. It does not connect to a queue.

That is the whole demo. Do not describe queue, retry, or dead-letter behavior as something this commit does.

## Target demo (not runnable)

This is the script Phase 6 and Phase 7 must make true. Until then it is a checklist, not a script.

1. Start SQL Server and RabbitMQ.
2. Start the API. Open Swagger.
3. Start one worker.
4. Submit several `demo.success` jobs. Watch them leave the queue and become `Succeeded`.
5. Watch the worker log: job id, attempt, duration.
6. Start a second worker.
7. Submit more jobs. Show that both workers consume (competing consumers, not a copy to each worker).
8. Submit `demo.transient-failure`. Show `RetryScheduled`, a growing delay, then success.
9. Submit `demo.permanent-failure`. Show `Failed` without a long retry loop.
10. Exhaust `maxAttempts`. Show `DeadLettered`.
11. Show a redelivered message for a job that is already `Succeeded`: the second worker acks and does not run the handler again.

Docker Compose is how steps 1–6 should start (`docker compose up`). The compose file is not in the repo yet.
