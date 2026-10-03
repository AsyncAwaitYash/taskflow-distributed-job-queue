# Docker

There is no Docker Compose file yet. Phase 6 adds one that runs SQL Server, RabbitMQ, the TaskFlow API, and at least two workers.

Until then:

- [docs/DEMO.md](../docs/DEMO.md) starts SQL Server and RabbitMQ with `docker run` and runs the API and two workers with `dotnet run`. That walkthrough was run and its output recorded.
- The integration tests start their own SQL Server and RabbitMQ containers through Testcontainers.

`.env.example` at the repository root lists the configuration the containers and processes need. Copy it to `.env` locally. Do not commit `.env`.
