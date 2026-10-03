# Security

This is a local learning project. It is not safe to expose on the public internet.

## In place now

- No secrets in the repository. `.env.example` has placeholders. `.gitignore` ignores `.env` and `.env.*`.
- The skeleton reads no connection string and no password.
- The skeleton logs no payload, because it has no payload.
- `Directory.Packages.props` pins package versions so a restore does not float to a surprise build.

## By design, not built

- No authentication or authorization. The master prompt excludes it. Anyone who can reach the port will be able to submit jobs once the API exists. Bind it to localhost.
- Input validation arrives with `POST /api/v1/jobs`: required type, known handler, payload size cap, `maxAttempts` bounds.
- Connection strings will come from the environment, not from a committed `appsettings` value.
- Logs must not include the SQL password, the RabbitMQ password, or the job payload.

## Limits to remember

- Swagger, when it exists, will describe an open local API.
- SQL Server in Docker is often configured with `TrustServerCertificate=true` for local dev. That is a local convenience, not a production setting.
- At-least-once delivery is a reliability property, not an authorization property. A redelivered message is still "the same job", not a security check.
