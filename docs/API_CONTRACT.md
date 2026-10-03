# API contract

Status: **not implemented.** The only live route is the Phase 0 probe below. Anything in the job contract is the target for Phase 2 and Phase 4.

Base path, when it exists: `/api/v1/jobs`.

Errors, when the job API exists: [Problem Details](https://datatracker.ietf.org/doc/html/rfc9457).

## Live today

### `GET /`

Returns 200 and a JSON object:

```json
{
  "name": "TaskFlow",
  "status": "skeleton",
  "phase": 0,
  "jobProcessing": false,
  "message": "Phase 0 skeleton. Job submission, RabbitMQ, and job handlers are not implemented."
}
```

`GET /api/v1/jobs` and `GET /health/live` return 404. That was checked on 2026-10-03.

## Planned

### `POST /api/v1/jobs`

Creates a job, stores it, and publishes a message. The response is the id and the initial status. It does not mean the handler has finished.

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
| type | Required. Must be a registered handler. Unknown types are rejected, not queued. |
| payload | Required JSON object. Size-limited. Not written to logs. |
| maxAttempts | Optional. Default comes from the retry policy. Must be at least 1 and within a configured cap. |
| correlationId | Optional. Generated when omitted. |

Response `201` with `id`, `status` (`Queued` if publish succeeded, otherwise the status the implementation actually persisted), `type`, and `correlationId`.

If SQL Server is down, the call fails and no message is published. If SQL Server commits and RabbitMQ publish fails, the response is an error and the row remains. That split is a documented limitation until the outbox exists. The response must not pretend the job was queued.

### `GET /api/v1/jobs`

Paged list. Filters: `status`, `type`, `createdFrom`, `createdTo`. Default sort: `CreatedAt` descending.

### `GET /api/v1/jobs/{id}`

The job plus its attempts. `404` when the id does not exist.

### `POST /api/v1/jobs/{id}/retry`

Manual retry of a `Failed` or `DeadLettered` job. Resets it onto the queue according to the policy. `409` when the status cannot transition.

### `POST /api/v1/jobs/{id}/cancel` (optional)

Not required for the first API slice. Add it only when the state machine has an explicit cancelled status.

## Job types (planned handlers)

| Type | Behavior |
| --- | --- |
| `demo.success` | Succeeds immediately |
| `demo.transient-failure` | Fails for the first N attempts, then succeeds |
| `demo.permanent-failure` | Always a permanent failure |
| `demo.slow` | Sleeps for a payload-specified duration |
| `email.send` | Simulated. No SMTP |
| `report.generate` | Simulated work |
| `data.process` | Simulated work |

## Status values

The domain enum `JobStatus` is `Pending`, `Queued`, `Processing`, `Succeeded`, `RetryScheduled`, `Failed`, `DeadLettered`.

Clients must not send a status. The server moves the job through `Job`. The HTTP API does not exist yet, so these values are not returned by any route except the unrelated `GET /` skeleton probe.
