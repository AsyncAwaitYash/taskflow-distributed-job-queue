# Docker

Docker Compose is not part of Phase 0.

Phase 6 adds a compose file that runs SQL Server, RabbitMQ, the TaskFlow API, and at least two workers. Do not add a compose file that pretends those services are wired up before the API and worker actually use them.

`.env.example` at the repository root lists the configuration those containers will need. Copy it to `.env` locally. Do not commit `.env`.
