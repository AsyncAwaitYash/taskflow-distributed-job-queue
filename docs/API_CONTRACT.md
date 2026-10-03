# API contract

Status: **create, list, and get are implemented.** Create publishes to RabbitMQ and returns `Queued` only after the broker confirms. A running `TaskFlow.Worker` then moves the job to `Processing` and then `Succeeded`, `Failed`, `RetryScheduled`, or `DeadLettered`; poll `GET /api/v1/jobs/{id}` to see it. The scheduler that republishes a due retry, the manual retry route, and cancel are not implemented.

Base path: `/api/v1/jobs`.

Errors: [Problem Details](https://datatracker.ietf.org/doc/html/rfc9457). In Problem Details, `status` is always the HTTP status code. The job's status, when present, is the `jobStatus` extension.

## Live today

### `GET /`

Returns 200 and a JSON object:

```json
{
  "name": "TaskFlow",
  "status": "running",
  "phase": 3,
  "jobProcessing": false,
  "databaseConfigured": true,
  "messagingConfigured": true,
  "message": "Jobs are stored in SQL Server and published to RabbitMQ. A separate TaskFlow.Worker process runs them."
}
```

`jobProcessing` is `false` because the API process never runs jobs. The worker is a separate process. `GET /health/live` still returns 404.

### `POST /api/v1/jobs`

```json
{
  "type": "demo.success",
  "payload": { "message": "hello" },
  "maxAttempts": 5,
  "correlationId": "optional"
}
```

| Field | Rules |
| --- | --- |
| type | Required. Must have a handler in `JobHandlerRegistry`. Unknown types are rejected, not stored. |
| payload | Required JSON object. Size-limited. Not written to logs and not put in the queue message. |
| maxAttempts | Optional. Default 5 (`TaskFlow:DefaultMaxAttempts`). Must be 1 to `TaskFlow:MaxAllowedAttempts` (20). |
| correlationId | Optional. Generated when omitted. |

Order of operations, in `JobSubmissionService`:

1. Validate. On failure: 400, nothing stored, nothing published.
2. Insert the row as `Pending` and commit.
3. Publish `{ "jobId", "type", "correlationId" }` to exchange `taskflow.jobs` with routing key `job.process`, and wait for the broker confirm.
4. Call `Job.MarkQueued` and save.
5. Return 201 with a `Location` header and the job. `status` is `Queued`.

| Response | When | Stored? | Published? |
| --- | --- | --- | --- |
| 201, `status: "Queued"` | Both writes and the confirm succeeded | Yes, `Queued` | Yes |
| 400 validation problem | Bad type, payload, budget, or correlation id | No | No |
| 503 `SQL Server is not configured.` | `ConnectionStrings:TaskFlow` missing | No | No |
| 503 `RabbitMQ is not configured.` | `ConnectionStrings:RabbitMq` missing | No | No |
| 503 `SQL Server is unavailable.` | The insert failed | No | No |
| 503 `RabbitMQ is unavailable.` with `jobId`, `jobStatus: "Pending"` | Insert committed, publish not confirmed (unreachable, nack, unroutable, or timeout) | Yes, `Pending` | No confirm |
| 503 `SQL Server is unavailable.` with `jobId`, `jobStatus: "Pending"` | Publish confirmed, the `Queued` save failed | Yes, `Pending` | Yes |

Example of a publish failure:

```json
{
  "title": "RabbitMQ is unavailable.",
  "status": 503,
  "detail": "The job was stored but not queued. It stays Pending and nothing republishes it yet.",
  "jobId": "1f0c...",
  "jobStatus": "Pending"
}
```

A `Pending` row is not republished by anything today. Retrying the HTTP call creates a second job. These are documented limits until an outbox or a republisher exists.

### `GET /api/v1/jobs`

Paged list. Filters: `status`, `type`, `createdFrom`, `createdTo`. Default sort: `CreatedAt` descending. Needs SQL Server only.

### `GET /api/v1/jobs/{id}`

The job plus its attempts. `404` when the id does not exist. Needs SQL Server only.

## Planned

### `POST /api/v1/jobs/{id}/retry`

Manual retry of a `Failed` or `DeadLettered` job. Resets it onto the queue according to the policy. `409` when the status cannot transition.

### `POST /api/v1/jobs/{id}/cancel` (optional)

Not required for the first API slice. Add it only when the state machine has an explicit cancelled status.

## Job types

The API accepts exactly the types that have a handler. The handlers live in `src/TaskFlow.Application/Jobs/Handlers`.

| Type | Behavior | State |
| --- | --- | --- |
| `demo.success` | Succeeds immediately | Implemented |
| `demo.permanent-failure` | Always fails permanently with `DemoPermanentFailure` | Implemented |
| `demo.slow` | Waits `seconds` (whole number, 0–30) from the payload, then succeeds. Anything else fails permanently with `InvalidPayload` | Implemented |
| `email.send` | Simulated. Needs a non-empty `to` string, otherwise `InvalidPayload`. No SMTP. The recipient is not logged | Implemented |
| `report.generate` | Simulated. Succeeds | Implemented |
| `data.process` | Simulated. Succeeds | Implemented |
| `demo.transient-failure` | Payload `{ "failTimes": 0-20 }`. Retryable failure for that many attempts, then success. A bad payload is `InvalidPayload` and the job is `Failed`. Nothing republishes a `RetryScheduled` job yet | Implemented |

A handler exception is stored as its type name in `errorType`. The exception message is not stored. Bad input is `Failed`; other exceptions are retryable (see Statuses).

## Status values

The domain enum `JobStatus` is `Pending`, `Queued`, `Processing`, `Succeeded`, `RetryScheduled`, `Failed`, `DeadLettered`.

Clients must not send a status. The server moves the job through `Job`. `GET` can return `Pending`, `Queued`, `Processing`, `Succeeded`, `Failed`, `RetryScheduled` (with `nextAttemptAt`), and `DeadLettered`. A handler exception is stored as its type name. Bad input (`JsonException`, `ArgumentException`, `FormatException`, `NotSupportedException`) is `Failed`. Anything else is `RetryScheduled` until the attempt budget runs out, then `DeadLettered`.
