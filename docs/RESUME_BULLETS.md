# Resume bullets

**None of these are earned yet.** Phase 0 is a skeleton. Do not put TaskFlow on a resume until the behavior in the bullet is implemented and tested.

Drafts to revisit after the matching phase:

- After Phases 2–3: Designed a .NET background-job pipeline that records jobs in SQL Server and delivers work through RabbitMQ to competing workers.
- After Phases 3–5: Used manual acknowledgements and database claims so a worker crash redelivers work without running a finished job a second time.
- After Phase 4: Classified transient and permanent failures and scheduled retries with exponential backoff and jitter, dead-lettering jobs that exhaust their attempts.
- After Phase 7: Covered duplicate delivery, worker races, and broker or database outages with integration tests.

When a bullet becomes true, delete the "not earned" warning for that line and point it at the test that proves it. Do not add numbers you did not measure.
