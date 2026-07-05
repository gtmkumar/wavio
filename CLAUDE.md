# Wavio — Project Instructions (always loaded)

This file holds the stable, always-true rules for the Wavio WhatsApp platform core.
It is read before any session prompt or subagent runs. Task-specific instructions
belong in the prompt or a skill, not here.

## Stack (canonical)

- **.NET 10** multi-service solution at `src/backend/wavio/` (core, commerce, operations services + Gateway (YARP) + AppHost + ServiceDefaults), .NET Aspire for local orchestration, RabbitMQ messaging.
- **PostgreSQL 16** — single database `waplatform`, DDD schema split, **RLS + `app.tenant_id` GUC on every tenant-scoped table** (non-negotiable).
- **Schema = hand-written versioned SQL migrations** (`V001__…`), sqlfluff-clean, validated in CI against real Postgres with an FK-audit gate. The versioned migrations are the only source of truth for schema.
- Canonical requirements: `docs/WHATSAPP_PLATFORM_CORE_PRODUCTION_SPEC.md`. Waves/constraints: `docs/BUILD_PLAN.md`.

## Reuse First — Never Duplicate Code, Files, or Tables

Before creating anything, search for what already exists and reuse or extend it. Recreating what is already there is a defect, not progress.

- **Code**: call the existing function/service/helper/component; extract shared logic instead of copy-pasting. No near-identical blocks.
- **Files**: edit the correct existing file in place. No parallel `v2`, `-copy`, `-new`, or backup files.
- **Tables & migrations**: never redefine or re-add an existing table/column. Extend the canonical versioned SQL migrations; no redundant tables.
- **Docs / config / tests**: update the existing section instead of adding a competing duplicate.

If duplication truly seems required, stop and justify why reuse won't work before writing it.

## How to work (prompt & delivery discipline)

- **Read existing conventions before writing.** Study the most recent comparable work (feature folder, component, migration, test) and match its patterns exactly — even if you'd choose differently. Don't introduce a new pattern, package, or folder convention without flagging it first.
- **Pair every "don't" with a "do".** State the banned path and its replacement together ("no X — use Y instead").
- **Don't default to the popular option.** Popularity is not justification. Don't agree with a framing by default — surface honest tradeoffs and recommend.
- **Verify, don't trust memory.** Confirm package versions, APIs, and facts against the actual files / official sources — never quote them from memory.
- **Approval & verification gates.** For large or risky work, show the plan (folder tree / schema / migration SQL / diff) and wait for approval before generating. Prove it works by exercising real behavior, then report what you ran and observed. If tests fail or a step was skipped, say so plainly.

## Backend guardrails (.NET)

Avoid the failure mode of unrequested *initiative* — reaching for statistically common .NET choices nobody asked for (MediatR, AutoMapper, repository/unit-of-work layers, try/catch wallpaper). Don't add them without per-feature justification.

- **Errors**: no try/catch wallpaper in endpoints — a global `IExceptionHandler` returning RFC 9457 ProblemDetails owns errors.
- **Schema**: versioned SQL migrations are the only schema path — never `EnsureCreated`, never redefine schema ad hoc. Review migration SQL before it touches a database.
- **EF Core**: one `IEntityTypeConfiguration` per entity (no giant `OnModelCreating`); never return `IQueryable` from a public method; never enable lazy-loading proxies; every async call takes a `CancellationToken`; read-only queries use `AsNoTracking` unless the handler mutates.
- **Data access**: parameterized queries / ORM params only — never string-concatenated SQL. Respect RLS + `app.tenant_id` on every tenant-scoped query.
- **Testing**: prefer real objects and hand-rolled fakes; mock only where an interface genuinely needs it. Integration tests use a real Postgres (Testcontainers / WebApplicationFactory) — never the EF Core InMemory provider. Name tests `Method_Scenario_ExpectedOutcome`; one behavior per test; assert observable behavior, not logs or private state. If a class is hard to test, stop and flag it as a design smell.
- **Security**: never log request bodies, tokens, secrets, or connection strings; never hardcode secrets or keep old connection strings "as fallback" — one source of truth.

## Turning repetition into assets

When an instruction repeats, promote it: project-wide conventions → this `CLAUDE.md`; repeatable workflows → a skill (`SKILL.md` / slash command). Treat prompts and skills as version-controlled code.
