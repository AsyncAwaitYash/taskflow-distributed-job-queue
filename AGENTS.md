# AGENTS.md

TaskFlow is a learning project: a distributed background job queue on .NET 10, ASP.NET Core, SQL Server, RabbitMQ, and a separate worker process.

The repository is the source of truth. Do not depend on chat history. Do not implement features that the docs still mark as planned and then describe them as done.

## Truth hierarchy

1. Source code and tests
2. Project documentation
3. Git history
4. The current conversation

When docs and code disagree, inspect the code and tests, then fix the docs.

## Before coding

1. Read this file.
2. Read `docs/CURRENT_STATE.md`.
3. Read `docs/MASTER_PLAN.md`.
4. Read `docs/PROJECT_CONTEXT.md`.
5. Read the docs that match the task (`ARCHITECTURE`, `DATABASE`, `API_CONTRACT`, `DECISIONS`, `TESTING`, `LEARNING_GUIDE`).
6. Inspect the relevant source and tests.

## How to work

- Implement one next incomplete task. Do not build the rest of the project in the same change.
- Keep controllers thin. Keep RabbitMQ and SQL Server out of the domain.
- Domain does not reference Infrastructure. Application does not reference Infrastructure.
- Do not acknowledge a RabbitMQ message before the job outcome is recorded, once messaging exists.
- Do not requeue forever. Retries go through the TaskFlow retry policy.
- Do not claim exactly-once execution.
- Do not add an outbox until Phases 0–7 are stable.
- Do not add authentication, a frontend, Redis, Kafka, or Kubernetes.
- Never commit secrets. Configuration comes from environment variables and `appsettings`. `.env.example` holds placeholders only.
- Mocks belong in tests. Production endpoints must not return fake jobs.

## Definition of done

A task is done only when the code exists, the build passes, the relevant tests pass, failure behavior is handled, and these docs are updated:

- `docs/CURRENT_STATE.md`
- `docs/LEARNING_GUIDE.md` (required after every meaningful task)
- `docs/CHANGELOG.md` when the change is user-visible
- `docs/DECISIONS.md` when the architecture changes

Generated code is not evidence. Build and test results are.

## After the change

Report what changed, what the user should learn, the build and test result, and the next task.

Phase 3 is complete. The next task is Phase 4 task 1: classify failures as retryable or permanent, add a configured exponential backoff with jitter policy, and have the worker record `RetryScheduled` with `NextAttemptAt` through `Job.RecordRetryableFailure` (or `DeadLettered` when attempts run out). Add the `demo.transient-failure` handler. Ack the message after that save; do not requeue. Do not build the scheduler that republishes due jobs, the retry endpoint, or the Phase 5 conditional claim in that change.
