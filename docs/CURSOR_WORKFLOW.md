# Cursor workflow

When the user says "Continue TaskFlow":

1. Read `AGENTS.md`.
2. Read `docs/CURRENT_STATE.md`.
3. Read `docs/MASTER_PLAN.md`.
4. Read `docs/PROJECT_CONTEXT.md`.
5. Read the docs for the area you will touch.
6. Inspect the current source and tests.
7. Pick the single next incomplete task. Today that is the Phase 1 domain model.
8. Implement only that task.
9. Run the relevant tests and `dotnet build TaskFlow.slnx`.
10. Review the diff. Revert unrelated edits.
11. Update `docs/LEARNING_GUIDE.md` with a task section tied to the files you changed.
12. Update `docs/CURRENT_STATE.md`.
13. Update `docs/CHANGELOG.md` when the behavior is visible.
14. Update `docs/DECISIONS.md` when an ADR changes.
15. Report what changed, what to learn, the build and test result, and the next task.

Do not ask the user to paste earlier chats or generated files. If a detail is missing and the master prompt already chose it, use that choice.

Do not implement Phase 8 early. Do not add a fake in-memory queue and call it RabbitMQ.
