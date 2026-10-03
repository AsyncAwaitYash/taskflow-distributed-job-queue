# Security

This is a local learning project. It is not safe to expose on the public internet.

## In place now

- No secrets in the repository. `.env.example` has placeholders. `.gitignore` ignores `.env` and `.env.*`.
- The API reads `ConnectionStrings:TaskFlow` and `ConnectionStrings:RabbitMq` from configuration. No password is committed. `.env.example` has placeholders.
- The RabbitMQ URI is validated as `amqp://` or `amqps://` at startup. The error message names the setting, not its value, so the password is not printed.
- Create and query logs include the job id, type, status, and correlation id. They do not include the payload. EF command logging is Warning so SQL parameter values stay out of the default log.
- Publish-failure logs include the job id and the exception type only. They do not include the exception message, which can contain the broker host.
- The queue message carries `jobId`, `type`, and `correlationId`. The payload stays in SQL Server. `JobPublishingTests` checks that a payload value is not in the message body.
- The worker logs the job id, outcome, worker id, correlation id, and duration. It does not log the payload or the message body, including when it rejects an unreadable message.
- Handlers do not log payload values. `email.send` checks for a recipient and never writes it anywhere.
- A handler exception is stored as its type name only. The exception message is not saved to `LastError` or the attempt, because it can echo payload values.
- The worker's startup error names a missing setting, not its value.
- `POST /api/v1/jobs` rejects unknown types, non-object payloads, oversized payloads, and out-of-range `maxAttempts`.
- `Directory.Packages.props` pins package versions so a restore does not float to a surprise build.

## By design, not built

- No authentication or authorization. The master prompt excludes it. Anyone who can reach the port can submit jobs. Bind it to localhost.
- No TLS to RabbitMQ in the local setup. `amqps://` is accepted by the configuration check but has not been tested.
- Logs must not include the SQL password, the RabbitMQ password, or the job payload.

## Limits to remember

- Swagger describes an open local API.
- SQL Server in Docker is often configured with `TrustServerCertificate=true` for local dev. That is a local convenience, not a production setting.
- RabbitMQ's default `guest` user only works from localhost, and its password is public. Use it only for local experiments. The integration tests use a throwaway container with its own credentials.
- At-least-once delivery is a reliability property, not an authorization property. A redelivered message is still "the same job", not a security check.
