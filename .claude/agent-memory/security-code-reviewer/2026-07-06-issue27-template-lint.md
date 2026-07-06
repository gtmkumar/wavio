---
name: 2026-07-06-issue27-template-lint
description: Security audit of issue #27 template policy lint v1 (working tree, develop) — APPROVE; LLM lint fail-open verified acceptable; Should-fixes = options ValidateOnStart, x-api-key log redaction, ParseVerdict catch too narrow, findings-text XSS-at-render note
metadata:
  type: project
---

# Issue #27 audit (2026-07-06) — template policy lint v1 (wa-admin)

Verdict: APPROVE (should-fixes non-blocking). Reviewed uncommitted working tree on develop; build/tests green per main session.

## Verified-good (don't re-flag)
- **Single lint gate**: only `TemplateSubmissionService` references `IWhatsAppTemplateGraphClient`; both submit paths (CreateTemplateCommandHandler + SubmitTemplateCommandHandler) call `SubmitAsync`, which runs every registered `ITemplateLintService`, persists one template_lint_results row per linter (TenantId from templateToSubmit under RLS WITH CHECK), and withholds Graph call on any block. Lint rows durable even when blocking (tested).
- **LlmTemplateLintService secrets hygiene**: ApiKey only as per-request `x-api-key` header (never DefaultRequestHeaders, never logged); LoggerMessage templates carry status code / exception TYPE only; request/response bodies never logged; appsettings carry only `Lint:Llm:Enabled=false` — no key anywhere. BaseUrl config-only → no SSRF.
- **Anthropic request shape verified via claude-api skill** (not memory): `output_config.format` json_schema is the current structured-outputs param; `claude-opus-4-8` is a valid current alias. So the call won't silently 400-and-skip due to API drift.
- **Timeout/cancellation**: linked CTS + `CancelAfter(TimeoutSeconds=15)`; catch filter correctly rethrows caller cancellation, swallows timeout OCE; SendAsync default ResponseContentRead buffers body within the CTS window. Refusal stop_reason → skip.
- **Fail-open posture acceptable**: LLM skip = non-blocking info finding; rules linter still gates, Meta review is the real gate; malicious LLM verdict worst case = wrong pass (Meta reviews) or self-block; max_tokens 1024 bounds stored findings. 7 tests cover disabled/transport/500/unparseable/refusal/pass/fail.
- **RulesTemplateLintService**: both regexes linear (`\{\{([^{}]*)\}\}`, `[!?]{3,}`) — no catastrophic backtracking; componentsJson is server-compiled (TemplateDefinitionCompiler), not raw user JSON.
- **TemplatePackSeeder**: fully parameterized NpgsqlCommand; inputs are compile-time constants (VerticalTemplatePacks) — nothing tenant-controlled; idempotent ON CONFLICT matches V009's NULLS-NOT-DISTINCT unique index; Admin-connection + boot-guard pattern identical to RetentionPolicySeeder.
- **Metrics endpoint**: `permission:templates.list`, RLS-only scoping (no explicit tenant filter) — consistent with GetTemplates; platform_admin sees cross-tenant aggregates via `app.is_platform_admin()` in policy, consistent with neighbors; counts only.

## Should-fixes reported (recur-watch)
1. **Silent fail-open config** (recurring theme — cf. issue-42 fingerprint secret, PR #45 Meta:Graph boot guard): `Enabled=true` + empty ApiKey → permanent unlogged skip (LlmTemplateLintService.cs:81 has no LogWarning); invalid BaseUrl → `new Uri()` throws per-request in DI factory → 500 on every submission. Fix: `AddOptionsWithValidateOnStart<LintLlmOptions>` (Enabled ⇒ ApiKey non-empty ∧ BaseUrl absolute https) + warn on skip.
2. **No `RedactLoggedHeaders(["x-api-key"])`** on the AddHttpClient chain — HttpClientFactory logs headers unredacted at Trace level. Repo has zero RedactLoggedHeaders usage; apply the pattern to any future typed client carrying secrets.
3. **ParseVerdict catch too narrow**: only JsonException; `AsArray()`/`GetValue<T>()` on wrong-shaped-but-valid JSON (e.g. proxy 200 `{"content":"x"}`) throws InvalidOperationException → escapes → 500, violating the documented degrade contract. Tests cover "not json" but not "valid JSON, wrong shape".
4. **Findings text = untrusted content rendered later**: rules interpolates raw tenant text (`match.Value` in FORMATTING_MALFORMED_PLACEHOLDER msg, unbounded length); LLM messages can carry prompt-injected content. JSON serialization is safe (no jsonb injection) — risk is XSS at client render. Frontend must render message/suggestedFix as plain text; consider truncating interpolated match text.

## Latent (backlog, pre-existing V009 — not this diff)
`templates.template_packs` policy WITH CHECK allows `tenant_id IS NULL` unconditionally → any tenant session could write/modify platform packs *if* an app write path existed. None does today (no EF entity; only the raw-ADO seeder). Tighten before shipping tenant-facing pack CRUD/adoption.
