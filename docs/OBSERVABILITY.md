# Observability

Status: **not implemented**, except the framework's default logs.

## Today

The API and the worker use the built-in console logger. The skeleton writes one structured information line with the layer names and `JobProcessingEnabled=false`. There is no Serilog, no correlation id middleware, no `/health/live`, no `/health/ready`, and no OpenTelemetry.

`GET /` is a liveness probe for humans during Phase 0. It is not the health contract.

## Planned (Phase 6)

Serilog, structured. Every processing log includes, when the value exists:

- JobId
- AttemptNumber
- WorkerId
- CorrelationId
- Status
- Duration

Do not log the payload. Do not log connection strings.

Correlation id: accept an incoming id, otherwise generate one, store it on the job, and put it on the message and the logs.

Health:

- `/health/live` — process is up
- `/health/ready` — SQL Server and RabbitMQ are reachable

OpenTelemetry comes after a job can actually succeed and fail. Metrics, only if the code records them:

- jobs submitted, started, succeeded, failed, retry scheduled, dead-lettered
- job duration
- queue processing

No metric is written down until something measures it. No invented throughput numbers.
