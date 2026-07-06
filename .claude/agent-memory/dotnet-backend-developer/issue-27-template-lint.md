---
name: issue-27-template-lint
description: What was built for issue #27 (template policy lint v1 — rules + LLM), the lint-gate design decision that fixed a real resubmit-path gap, and LLM degrade semantics
metadata:
  type: project
---

# Issue #27 — template policy lint v1 (2026-07-06)

Branch `develop`. Replaces the always-pass Wave 1 lint stub (issue #16) with a real static-ruleset
linter, an optional LLM second pass, seeded vertical template packs, and a first-pass
approval-rate metric endpoint. See also [[issue-16-template-lifecycle]] (the pipeline this extends)
and [[cqrs-validation-pipeline-is-dead-code]] (why guard clauses, not FluentValidation, stayed the
pattern here too — no validator classes were added for this issue).

## What was built

- **`WaAdmin.Application/Common/Interfaces/ITemplateLintService.cs`**: extended with a
  `TemplateLintInput(Category, Language, ComponentsJson)` record — linting needs the template's
  category (utility/marketing rules differ) and language, which the old `LintAsync(string
  componentsJson, ...)` signature didn't carry. Every implementation and caller updated together.
- **`WaAdmin.Infrastructure/Templates/RulesTemplateLintService.cs`** (Linter = "rules"): static
  ruleset — promotional language in utility templates, missing opt-out in marketing, variable
  leading/trailing + density heuristics, formatting (empty body, all-caps, excessive
  punctuation/emoji, malformed `{{n}}` placeholders). All findings are "error" severity (blocking) —
  a deliberate v1 simplification matching the issue's own acceptance criterion that every listed
  known-bad category must block pre-submission; no "warning" tier was added.
- **`WaAdmin.Infrastructure/Templates/LlmTemplateLintService.cs`** (Linter = "llm", optional):
  calls the Anthropic Messages API (`POST /v1/messages`) with plain `HttpClient` (no SDK package),
  using `output_config.format` (structured outputs / JSON schema) so a successful call is
  guaranteed valid JSON — no markdown-fence stripping needed. Bound from `Lint:Llm` via
  `LintLlmOptions` (Enabled=false default). **Degrade semantics (documented, not incidental):** ANY
  transport failure, non-2xx, refusal (`stop_reason: "refusal"`), or unparseable body returns a
  **non-blocking** "skipped" outcome (`Passed=true`, one "info"-severity `LLM_SKIPPED` finding) —
  the LLM pass can never crash or block submission just because Anthropic is unreachable. Only the
  model's own verdict can produce a blocking finding. Never logs the API key or template content —
  transport-failure logs record only the exception *type*.
- **`WaAdmin.Infrastructure/Templates/VerticalTemplatePacks.cs`** + `Seeders/TemplatePackSeeder.cs`:
  5 packs (appointment reminder, pickup scheduled, order ready, payment link — all utility; OTP —
  authentication) seeded as NULL-tenant rows into `templates.template_packs`, following
  `RetentionPolicySeeder`'s exact pattern (privileged Admin connection, raw ADO.NET, runs in every
  environment since it's reference content not a secret, idempotent via
  `ON CONFLICT (tenant_id, pack_key) DO NOTHING`). No new migration — `template_packs` already
  existed in V009.
- **`GetTemplateApprovalMetricsQuery`** (`GET /v1/templates/metrics/approval-rate`, permission
  `templates.list` reused — no new permission catalog entry added): first-pass approval rate +
  lint pass-rate, scoped entirely via RLS (no explicit tenant filter), same convention as
  `GetTemplatesQueryHandler`.
- **Tests**: `tests/WaAdmin.Tests/Lint/` (Rules table-driven fixtures incl. every known-bad
  category, Llm degrade-path fixtures with a hand-rolled `HttpMessageHandler` fake, vertical-pack
  lint-clean proof), `Templates/SubmitTemplateCommandHandlerTests.cs` (new — see gap below),
  `Templates/GetTemplateApprovalMetricsQueryHandlerTests.cs`. 193 WaAdmin.Tests pass; full solution
  (wavio.Utilities.Tests, WaGateway/WaBilling/WaIngest/WaIntel.Tests) and the real-Postgres
  `WaPlatform.IntegrationTests` tier (Testcontainers, Colima) all green.

## Design decision: lint gate moved into `TemplateSubmissionService`, not left in `CreateTemplateCommandHandler`

The orchestrator asked me to verify how the handler currently treated a failed lint. Before this
issue, `CreateTemplateCommandHandler` ran the lint itself and skipped `SubmitAsync` on failure —
but `SubmitTemplateCommandHandler` (the standalone `POST /v1/templates/{id}/submit` resubmit path)
called `TemplateSubmissionService.SubmitAsync` **directly, with no lint call at all**. A template
edited via `UpdateTemplateCommandHandler` (which creates a new DRAFT version, no lint) and then
explicitly resubmitted skipped linting entirely — a real acceptance-criterion gap, not a hypothetical
one (the issue requires known-bad content blocked "pre-submission", full stop, not just on the
create path).

Fix: moved lint execution entirely into `TemplateSubmissionService.SubmitAsync` (constructor now
takes `IEnumerable<ITemplateLintService>`), which both `CreateTemplateCommandHandler` and
`SubmitTemplateCommandHandler` already funnel through — `ITemplateSubmissionService`'s own doc
comment already said "so the transition logic lives in exactly one place," which was the tell that
lint gating (a precondition of that same transition) belonged there too. This let
`CreateTemplateCommandHandler` drop its `ITemplateLintService` dependency and the manual
lint-then-insert dance entirely — a real simplification, not scope creep, since duplicating the
gate in two handlers would have been the actual anti-pattern. `IEnumerable<ITemplateLintService>`
resolves automatically from every DI registration of that interface (no explicit `IEnumerable`
registration needed) — Rules is always registered; Llm is registered conditionally on
`Lint:Llm:Enabled` in `WaAdmin.Infrastructure/DependencyInjection.cs`, so "disabled" is rules-only
by omission from the collection, not a runtime branch.

**How to apply**: before adding a policy/validation gate to one CQRS handler, check whether a
sibling handler reaches the same downstream side effect through a shared service — if so, the gate
belongs in the shared service, not duplicated (or worse, only applied on one path).

## Live-found: a second call site outside WaAdmin.Tests needed the constructor-signature fix

`tests/WaPlatform.IntegrationTests/Templates/CreateTemplateCircularFkTests.cs` (issue #46's
real-Postgres regression test for the EF circular-FK save-ordering bug — see
[[issue-46-integration-tests]]) also constructs `TemplateSubmissionService`/`CreateTemplateCommandHandler`
directly and was **not** caught by grepping `tests/WaAdmin.Tests/` alone — it lives in the separate
`WaPlatform.IntegrationTests` project. Only surfaced when building/running that project explicitly.
**How to apply**: when changing a constructor signature, grep the *whole* `tests/` tree (and any
other project-external call sites) for the type name, not just the primary unit-test project —
integration-test projects construct production types directly too.
