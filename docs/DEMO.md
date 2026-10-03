# Demo

## What you can show today

Two worker processes sharing one queue, end to end, on Windows PowerShell with Docker. There is no Docker Compose file yet (Phase 6), so the infrastructure starts with `docker run`.

Verified on 2026-10-03 on Windows 10 with Docker 29.8.1 and SDK 10.0.401. The results quoted below are what that run printed.

### 1. Start SQL Server and RabbitMQ

Pick a local-only password. SQL Server requires upper case, lower case, a digit, and a symbol.

```powershell
$pw = 'Choose-a-local-Passw0rd'
docker run -d --name taskflow-sql -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=$pw" -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
docker run -d --name taskflow-rabbitmq -e RABBITMQ_DEFAULT_USER=taskflow -e "RABBITMQ_DEFAULT_PASS=$pw" -p 5672:5672 -p 15672:15672 rabbitmq:4.1-management
```

Use a named RabbitMQ user. The built-in `guest` user only accepts connections from inside the container, and a port-mapped connection does not count.

### 2. Set the connection strings

Run this in every terminal you open below, after setting `$pw` the same way.

```powershell
$env:ConnectionStrings__TaskFlow = "Server=localhost,1433;Database=TaskFlow;User Id=sa;Password=$pw;TrustServerCertificate=True"
$env:ConnectionStrings__RabbitMq = "amqp://taskflow:$pw@localhost:5672/"
```

### 3. Create the database

```powershell
dotnet build TaskFlow.slnx
dotnet tool restore
dotnet ef database update --project src/TaskFlow.Infrastructure --startup-project src/TaskFlow.Api
```

Observed: `InitialJobSchema`, `JobClientGeneratedIds`, and `EnableReadCommittedSnapshot` applied, then `Done.` Wait about 15 seconds after `docker run` if SQL Server refuses the first connection.

### 4. Start the API and two workers

One terminal each:

```powershell
dotnet run --project src/TaskFlow.Api
dotnet run --project src/TaskFlow.Worker --launch-profile worker-1
dotnet run --project src/TaskFlow.Worker --launch-profile worker-2
```

Observed in each worker: `TaskFlow worker consuming. WorkerId=worker-1 Queue=taskflow.jobs.process PrefetchCount=1` (and `worker-2`). The profiles only set `TaskFlow__Worker__WorkerId`. The connection strings come from step 2.

### 5. Submit six slow jobs

In a fourth terminal:

```powershell
$ids = 1..6 | ForEach-Object {
  (Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/v1/jobs -ContentType 'application/json' -Body '{"type":"demo.slow","payload":{"seconds":2}}').id
}
```

Open `http://localhost:15672` (user `taskflow`, your password), Queues, `taskflow.jobs.process`. Observed: Consumers 2.

### 6. See who ran what

```powershell
Start-Sleep 10
$ids | ForEach-Object {
  $j = Invoke-RestMethod "http://localhost:8080/api/v1/jobs/$_"
  "$($j.status) attempts=$($j.attempts.Count) worker=$($j.attempts[0].workerId) ms=$($j.attempts[0].durationMilliseconds)"
}
```

Observed:

```text
Succeeded attempts=1 worker=worker-1 ms=2173
Succeeded attempts=1 worker=worker-2 ms=2163
Succeeded attempts=1 worker=worker-1 ms=2009
Succeeded attempts=1 worker=worker-2 ms=2020
Succeeded attempts=1 worker=worker-1 ms=2014
Succeeded attempts=1 worker=worker-2 ms=2016
```

Each message went to one worker. With prefetch 1, a busy worker is not offered a second message, so the next one goes to the idle worker. Each worker log has a `Processed delivery. JobId=... Outcome=Succeeded ... WorkerId=...` line per job. The alternating order is what this run showed; RabbitMQ does not promise a strict order.

### 7. A permanent failure

```powershell
$f = Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/v1/jobs -ContentType 'application/json' -Body '{"type":"demo.permanent-failure","payload":{}}'
Start-Sleep 2
Invoke-RestMethod "http://localhost:8080/api/v1/jobs/$($f.id)"
```

Observed: `status` `Failed`, `attempts[0].errorType` `DemoPermanentFailure`, `lastError` `demo.permanent-failure always fails.` One attempt. No retry loop.

### 8. Stop one worker

Stop `worker-2` (Ctrl+C in its terminal; the verified run killed the process). Submit three `demo.success` jobs the same way as step 5. Observed: all three `Succeeded` with `worker=worker-1`. The management UI kept showing 2 consumers for a few seconds before it showed 1. Its statistics refresh on an interval.

### 9. Clean up

```powershell
docker rm -f taskflow-sql taskflow-rabbitmq
Remove-Item Env:ConnectionStrings__TaskFlow, Env:ConnectionStrings__RabbitMq
```

Clear the variables before running `dotnet test` in the same terminal. Otherwise the tests that expect "not configured" see your demo settings and fail.

## Not in this demo

- Killing a worker in the middle of a job. Today the job would stay `Processing` (Phase 5).
- Retries. `demo.transient-failure` returns 400 until Phase 4.

## Planned demo

These steps become true in later phases. They are a checklist, not a script.

1. Phase 4: submit `demo.transient-failure`. Show `RetryScheduled`, a growing delay, then success.
2. Phase 4: exhaust `maxAttempts`. Show `DeadLettered`. Retry it through `POST /api/v1/jobs/{id}/retry`.
3. Phase 5: kill a worker mid-job and show the job recovered, not stuck in `Processing`.
4. Phase 6: `docker compose up` replaces steps 1–4 above, with structured logs and health checks.
