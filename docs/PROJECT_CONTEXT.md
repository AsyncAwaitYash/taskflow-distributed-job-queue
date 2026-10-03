# Project context

## What this is

TaskFlow is a portfolio project for a developer with about two years of experience who is preparing to change jobs. It is a small distributed background job queue. The point is depth: asynchronous processing, and reliable processing (acknowledgements, retries, idempotency, worker failure).

The finished system should be small enough to explain completely in an interview.

## What the user should be able to explain

- Why background jobs exist, and why the API should not do expensive work inline
- What RabbitMQ does here, and what the worker does
- What acknowledgement, redelivery, and retries mean
- How exponential backoff and jitter work, and why retry storms are dangerous
- What idempotency means, and why duplicate processing can happen
- How SQL Server stores job state
- How the API and worker interact, and which failure windows remain
- How the design could scale

## Non-goals

- A SaaS product, a frontend, or an auth system
- Redis, Kafka, Kubernetes, Elasticsearch, GraphQL, or AI APIs
- Real email delivery
- Exactly-once processing
- A transactional outbox before Phases 0–7 are stable

## Audience and runtime

Everything required to understand, continue, test, and learn the project lives in this repository. The app must run locally without paid services. Docker Compose is the intended developer experience once the services exist.

## Truth hierarchy

1. Source code and tests
2. These docs
3. Git history
4. A chat transcript

## Current milestone

Phase 0 is the milestone in the tree today: a buildable skeleton and this documentation set. The runtime flow below is the target, not the running system.

```text
Client
  -> POST /api/v1/jobs
  -> ASP.NET Core API
       -> SQL Server (job row)
       -> RabbitMQ
            -> Worker -> Job handler
                 -> SQL Server (terminal or retry state)
```

A second worker competes for the same queue. `CompetingConsumersTests` and `docs/DEMO.md` show that.
