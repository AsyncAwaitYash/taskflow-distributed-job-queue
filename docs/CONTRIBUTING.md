# Contributing

## Setup

Install the .NET 10 SDK. `global.json` asks for `10.0.401` and allows a later `10.0` feature band.

```bash
dotnet test TaskFlow.slnx
```

Copy `.env.example` to `.env` only when a later phase needs local secrets. Do not commit `.env`.

## Change rules

- One task from `docs/MASTER_PLAN.md` per change.
- Read `docs/CURRENT_STATE.md` first.
- Keep the dependency direction in `docs/ARCHITECTURE.md`.
- Update `docs/LEARNING_GUIDE.md` and `docs/CURRENT_STATE.md` in the same change.
- Add an ADR when the architecture changes. Do not add one for a rename.
- Prefer commit subjects like `feat: add job persistence` or `test: cover duplicate delivery`.

## Checks

`scripts/verify.sh` must pass. If the change touches the API or worker host, run the process and hit the route or read the log. Do not claim a queue behavior you did not execute.
