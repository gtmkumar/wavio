---
name: issue-16-template-lifecycle
description: What was built for issue #16 (wa-admin-svc template lifecycle), a real EF SaveChanges ordering bug live verification caught, and what's left for #14/#19/#22/#27
metadata:
  type: project
---

# Issue #16 — template lifecycle v1 (2026-07-03/07-04)

Branch `feature/16-template-lifecycle` off `feature/13-ingest-webhooks` (stacked: #37 → #39 →
#41 → this). PR body: "Closes #16" + "stacked on #41 → #39 → #37".

## What was built
- **Entities/config** (`wavio.SharedDataModel/Entities/Templates`,
  `Persistence/Configurations/Templates`): Template, TemplateVersion, TemplateStatusEvent,
  TemplateCategoryChange, TemplateLintResult — mapped onto V009's `templates` schema
  (database-first, no EF migrations). `WavioDbContext` DbSets added.
- **WaAdmin.Application/Templates**: `TemplateStatusTransitions` (pure state-machine guard),
  `TemplateAutoPauseSchedule` (3h/6h/disabled escalation), `TemplateDefinitionCompiler` (DSL →
  Meta component JSON), `TemplateSubmissionService` (shared DRAFT→PENDING submit logic used by
  both create and resubmit), commands (CreateTemplate, UpdateTemplate, SubmitTemplate,
  DeleteTemplate, ProcessTemplateStatusChanged, ProcessTemplateCategoryChanged), queries
  (GetTemplates, GetTemplateById, GetTemplateStatus).
- **WaAdmin.Infrastructure**: `WaAdminDbContext` adapter (+ a raw `Database.SqlQuery` lookup for
  `waba.business_accounts.meta_waba_id` — no EF entity for the waba schema yet, WABA onboarding is
  #6/#14); `StubTemplateLintService` (always-pass); `NoOpCampaignFreezeHook` / no-op honest stub
  for #22, `NoOpBillingRecalibrationHook` / honest stub for #19, `LoggingTenantAlertPublisher`
  (structured log, no real channel yet); `MetaGraphTemplateClient` (typed HttpClient against
  `Meta:Graph:BaseUrl`); `ScopedCurrentTenant` (serves both HTTP requests and the background
  consumer's per-message DI scope — see gotcha below); `TemplateEventsConsumerBackgroundService`
  (consumes `wa.template.status_changed.v1` / `wa.template.category_changed.v1` from the shared
  `wavio.events` exchange; see the security-review follow-up section below for the
  transient-vs-permanent-failure ack logic, which changed after the initial PR).
- **WaAdmin.WebApi/Endpoints/Templates.cs**: `/v1/templates` CRUD + `/status` + `/submit`, gated
  on new `templates.{list,read,create,update,submit,delete}` permissions (seeded in
  `core.Infrastructure/Seeders/IdentitySeeder.cs`; `templates.delete` is High risk → step-up
  required, confirmed live).
- **tools/MetaGraphApiStub**: standalone minimal-API stub server for Meta's template-submit
  endpoint (`reject_*` name prefix → 400, `error500_*` → 500, else → 201 deterministic fake id).
  Not used by the automated test suite (that fakes the HTTP layer directly) — this is the literal
  dev/manual-verification stub.
- **tests/WaAdmin.Tests** (81 tests, all passing): table-driven state-machine tests (every
  legal/illegal (from,to) pair), auto-pause schedule tests, handler tests for all 6
  commands/consumers (in-memory EF + Moq), thin endpoint tests (static method + fake
  ICurrentUser/IDispatcher, same pattern as WaIngest.Tests), RabbitMqConnectionManager fail-closed
  tests.

## A real bug live verification caught (in-memory tests did NOT)
EF's default SaveChanges statement ordering is **not** add-order when multiple new entities are
batched in one SaveChanges call and there's no EF-modeled relationship between them — confirmed
against real Postgres, not a theory. The systemwide `AuditSaveChangesInterceptor`
(`wavio.Utilities/Auth/Audit`) auto-adds an `audit_logs` insert for every Added/Modified/Deleted
entity in the same batch, and that extra traffic was enough to reorder my `TemplateVersion` insert
*before* its parent `Template` insert — tripping `template_versions_template_id_fkey` (and, before
that fix, `template_lint_results_template_version_id_fkey`). The EF InMemory provider used by unit
tests does **not enforce FKs at all**, so all 81 unit tests passed while the real endpoint 500'd.

**Fix, in two parts:**
1. Added real EF relationships (`HasOne<T>().WithMany().HasForeignKey(...)`) for every
   cross-entity FK in the templates schema (TemplateVersion→Template,
   TemplateLintResult/TemplateStatusEvent→TemplateVersion, TemplateStatusEvent/
   TemplateCategoryChange→Template) — no navigation properties added (entities stay plain
   POCOs), just enough for EF's SaveChanges dependency graph to order things correctly regardless
   of what the audit interceptor injects. This is the general fix for the whole templates schema,
   not just Create.
2. **Exception**: `Template.CurrentVersionId ↔ TemplateVersion.TemplateId` is a genuine circular
   pair (both tables reference each other, matching the migration's own comment). Configuring
   *both* directions as EF relationships makes EF throw `InvalidOperationException: circular
   dependency detected` when a Template and its first Version are both newly Added in one
   SaveChanges — EF's automatic null-then-update cycle-breaking did **not** kick in here (tried,
   confirmed failing live). Left `Template→TemplateVersion` (CurrentVersionId) **unmodeled** in EF
   and instead split `CreateTemplateCommandHandler` into two SaveChanges calls around that one
   assignment (save Template+Version, THEN set `CurrentVersionId`, THEN save again). Documented in
   both `TemplateConfiguration`'s and the handler's comments so nobody "fixes" this into a
   relationship again and reintroduces the circular-dependency crash.

**Takeaway for future work in this schema**: any new code that inserts >1 new templates-schema
entity with a cross-reference in a single SaveChanges call needs either (a) a modeled EF
relationship (preferred, already done for every non-circular pair) or (b) an explicit
save-in-between for genuinely circular pairs. Don't trust EF InMemory tests alone to catch
ordering bugs — verify live against real Postgres before trusting a multi-entity SaveChanges call.

## Other things confirmed only by live verification
- **Permission gating for tenant vs platform-admin identity**: `HttpContextCurrentUser
  .RequireTenantId()` (the X-Tenant-Id-override-aware resolver used by endpoint handlers to stamp
  `TenantId` on new rows) and `HttpContextCurrentTenant.TenantId` (which the shared
  `RlsConnectionInterceptor` reads to set the `app.tenant_id` RLS GUC) are **two different
  resolution paths that disagree for platform_admin tokens** — `RequireTenantId()` honors the
  `X-Tenant-Id` header override, but `HttpContextCurrentTenant` does not, so a platform_admin
  calling a tenant-scoped write endpoint with `X-Tenant-Id` gets a clean-looking `TenantId` on the
  entity but an RLS `WITH CHECK` failure at the DB (surfaces as a generic "data error" 400, not an
  auth error, since `app_user` is never granted the Postgres `platform_admin` role — see
  `.claude/agent-memory/database-architect/decisions.md`: "bypass_rls GUC has no effect"). Worked
  around for my own verification by minting a genuine tenant-scoped user (real `tenant_id` JWT
  claim) instead of fighting the platform-admin + X-Tenant-Id path. **This is a pre-existing gap
  in shared `wavio.Utilities`/`wavio.SharedDataModel`, not something #16 introduced** — flagging
  for whoever next needs platform-admin cross-tenant writes to work (not just reads).
- Full lifecycle proven live against the compose stack (Postgres 16 + RabbitMQ) + a real WaAdmin
  .WebApi process + the MetaGraphApiStub: create→lint→submit (real HTTP 201, deterministic stub
  id) → PENDING; synthetic `wa.template.status_changed.v1` via RabbitMQ HTTP-API publish →
  PENDING→APPROVED→PAUSED(3h)→APPROVED→PAUSED(6h, pause_count escalation confirmed)→DISABLED, every
  transition in `template_status_events`; `wa.template.category_changed.v1` → category updated +
  `tenant_alerted_at`/`billing_recalibrated_at` both set; immutability (editing an APPROVED
  template creates version 2 as DRAFT, version 1's `components` column untouched — checked via
  psql, not just the API response); invalid transitions rejected both locally (422
  BusinessRuleException on submit/edit while PENDING) and consumer-side (DISABLED→APPROVED parked,
  confirmed landing in the DLQ via the RabbitMQ management API, not just logged).

## Shared-environment note (multi-agent)
Postgres/RabbitMQ containers were already running with another agent's issue #15 data
(`test-tenant-15`, a WABA row) when I started — did NOT recreate/wipe the stack, reused their
tenant/business-account row for my verification, and cleaned up my own test templates + test user
afterward (left the permission-catalog additions, which are real feature rows both agents' seeded
DBs will end up with anyway). See `.claude/agent-memory/dotnet-backend-developer/handoff.md` for
the general policy this confirms.

## Security-review follow-up (2026-07-04, after PR #44 opened)

Orchestrator relayed a security review with one important finding (S1) and one quick one (S2),
both fixed on the same branch before the PR entered the merge queue.

**S1 — the consumer's catch-all was turning transient failures into permanent data loss.** The
original `HandleDeliveryAsync` caught every exception and Nacked without requeue (straight to
DLQ) — meaning a brief Postgres blip during a real Meta status transition would permanently park
it, no automatic recovery. Fixed by extracting `TransientRetryPolicy`
(`WaAdmin.Infrastructure/Messaging/TransientRetryPolicy.cs`) — classifies exceptions and drives a
bounded (3 attempts, 1s/2s/4s backoff) in-process retry per message, each attempt in its own DI
scope (fresh `DbContext`, since a connection object may itself be faulted after a transient
failure — don't reuse it across retries). Outcome is one of `Processed` / `Requeue` (transient,
retries exhausted — nack WITH requeue, never DLQ) / `DeadLetter` (permanent: malformed payload, OR
the command handler's deterministic `false` "parked" return — unresolvable tenant/unknown
template/invalid transition; retrying either would never help).

**A second real bug found live while verifying the fix**: my first classifier (`ex is DbException
or TimeoutException or SocketException or IOException`) missed the ACTUAL exception a real
Postgres outage produces — `Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException`.
Turns out `AddSharedDataModel`'s Npgsql provider is configured with EF's own
`NpgsqlRetryingExecutionStrategy` (`EnableRetryOnFailure`) — it already retries 3 times internally
before giving up and wrapping the real Npgsql/socket exception in this wrapper type, which is
**not itself a `DbException`**. A same-type-only check on the outer exception dead-lettered a
perfectly recoverable outage on the first live test. Fixed by making `IsTransient` walk the full
`InnerException` chain instead of checking only the top-level type — this is the general, robust
fix (any future wrapper type with a transient cause underneath gets caught too), not a special
case for this one EF type.

**Live-verified the actual recovery cycle** (no shared-container risk — pointed a standalone
`WaAdmin.WebApi` at a deliberately wrong Postgres port instead of touching the shared
`wavio-postgres` container, so this never risked disrupting the other concurrent agent's work):
published a synthetic `wa.template.status_changed.v1` while "Postgres" was unreachable → logs
showed attempt 1/3, 2/3, 3/3 with growing delays → `Requeueing message ... after exhausting
in-process retries` → confirmed via the RabbitMQ management API that the DLQ count did NOT
increase and the message reappeared on the live `wa-admin.template-events` queue → pointed the
same process back at the real DB → the requeued message was redelivered and processed on the
very next attempt (this particular test template didn't exist, so it correctly parked as "unknown
template" this time — a real DeadLetter, not a transient one — proving the classifier
distinguishes the two cases correctly on the SAME message across a state change).
`tests/WaAdmin.Tests/Messaging/TransientRetryPolicyTests.cs` covers this deterministically
(fake operations, no real delay/broker/DB) — including a regression test for the
`RetryLimitExceededException`-shaped wrapper bug specifically.

Also added `WaAdmin.Infrastructure/Messaging/README.md` — the requested short DLQ-redrive runbook
(what lands there vs. what doesn't, how to inspect/re-inject via the RabbitMQ management API).

**S2 — resource-abuse hardening.** `GetTemplatesQueryHandler` now clamps `pageSize` to
`Math.Clamp(1, 200)` (was only floored at 1, never capped) — note `PaginatedList<T>.PageSize` is a
*private* property, so `GetTemplatesQueryHandlerTests` asserts on the observable page shape
(`List.Count`, `PageCount`) instead of the property directly. `WaAdmin.WebApi/Program.cs` now sets
`KestrelServerOptions.Limits.MaxRequestBodySize = 262_144` (256KB) — this host's only
body-accepting endpoints are template create/update, whose compiled component JSON is a few KB at
most; verified live that an oversized POST gets a clean 413 from Kestrel itself, before any
model binding or handler code runs (confirmed a request with NO auth header instead returns 401,
since the framework never reaches body-reading for an unauthenticated request — the 413 test
needs a valid, authorized token to actually exercise the size check).

## Left for later issues (by design, not oversight)
- #6/#14: real per-WABA Meta system-user token storage (envelope-encrypted); `MetaGraphOptions
  .AccessToken` is a single config value for now.
- #19: `NoOpBillingRecalibrationHook` → real billing recalculation on category change.
- #22: `NoOpCampaignFreezeHook` → real campaign freeze on PAUSED/DISABLED.
- #27: `ITemplateLintService` real rules/LLM implementations (stub always passes).
- A real tenant-alert notification channel (currently a structured log line only).
