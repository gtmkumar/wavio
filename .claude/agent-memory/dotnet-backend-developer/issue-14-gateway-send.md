---
name: issue-14-gateway-send
description: Issue #14 wa-gateway-svc outbound send API — window-state HTTP hop decision, ScopedCurrentTenant reuse, exactly-once limitation, and the Aspire Polly double-retry bug found live
metadata:
  type: project
---

Issue #14 (outbound send API: `POST /v1/messages`, idempotency, window-aware policy, transactional
outbox, retries, rate limiting) is built in **wa-gateway-svc** (`WaGateway.{Application,
Infrastructure,WebApi}`), on top of `messaging.outbound_messages`/`messaging.outbound_outbox`
(V007, already on this stack — no new migration needed).

**Window-state consultation (ADR-005):** used the HTTP hop to wa-intel-svc's
`GET /v1/windows/{waId}` (the orchestrator's stated default), NOT a direct read of the `sessions`
schema — that schema is owned by wa-intel-svc and a cross-service DB read would violate bounded-
context ownership. Auth is solved by forwarding the CALLER's own bearer token (no dedicated
service-to-service credential exists yet in Wave 1) — this only works because the client is only
ever invoked from within an active HTTP request (`SendMessageHandler`), never from the background
dispatcher. A short-TTL (5s) `IMemoryCache` keeps this within the p95 &lt;2s budget; measured
p95 ≈ 357ms locally against the stub Graph server for a 20-message burst (local-indicative, not a
production SLA proof).

**ScopedCurrentTenant reused from wa-admin-svc's issue #16, not from my own issue #15 fix:** issue
#15's Session Window Manager solved "background writer against RLS with no HttpContext" via
`IWaIntelDbContext.SetTenantContextAsync` (explicit `OpenConnectionAsync` + `set_config`) — see
[[rls-background-service-guc-gotcha]]. Found a CLEANER pattern already built for issue #16
(`WaAdmin.Infrastructure.ScopedCurrentTenant`, on PR #44's branch, not merged to my base): replace
`ICurrentTenant` itself (via `services.Replace(...)`) with a version carrying a settable
`OverrideTenantId`, so `RlsConnectionInterceptor` reads the correct tenant on every connection-open
regardless of how many times EF re-opens the connection — no need to reason about EF's implicit
open/close cycle at all. Reimplemented locally (not cross-merged) as
`WaGateway.Infrastructure.Persistence.ScopedCurrentTenant`. **Worth retrofitting into WaIntel's
`WindowClosingScannerService`/consumer at some point** — it's strictly simpler than the
`SetTenantContextAsync` workaround, but that's out of scope for this issue.

**Honest exactly-once limitation:** Meta's Cloud API messages endpoint has no client-supplied
idempotency key, so there's an unavoidable window between the Graph HTTP call succeeding and the
dispatcher's own DB write recording it, where a crash can cause a duplicate send on lease
reclaim. This is a Graph API constraint, not an implementation gap — documented in
`OutboxDispatcherService`'s class doc comment. What's guaranteed and was proven live: zero
message loss (a "crashed" instance's stale lease — status='dispatching', 9s-old locked_at — gets
reclaimed and completes to a wamid) and a bounded, minimized duplicate-send window (attempts
incremented durably BEFORE the Graph call, so retry counts survive a crash even though the exact
call outcome might not).

**Found live, not by unit tests (the in-memory test provider doesn't model HTTP resilience or
real DB constraints):**
- Aspire's `AddServiceDefaults()` applies a standard Polly resilience handler (its own internal
  retry-with-backoff) to EVERY typed HttpClient by default — including the Graph client. This
  silently retried a stub 500 THREE times inside what the outbox dispatcher counted as ONE
  attempt, desyncing `attempts` from the real number of Graph HTTP calls and undermining the
  "max 5 attempts" acceptance criterion's precision. Fixed with
  `.RemoveAllResilienceHandlers()` (experimental API, `EXTEXP0001`, deliberately suppressed with
  a comment) on just the Graph client — other clients keep the platform default. Any future
  service adding its own deliberate HTTP-level retry policy on top of a typed HttpClient needs
  this same fix, or its retry counts will be wrong without any visible symptom other than "why did
  the stub get called more times than expected."
- `MessageSendFailedV1.PhoneNumberId` was initially populated from the internal GUID
  (`entry.PhoneNumberId.ToString()`) instead of Meta's raw phone_number_id string, inconsistent
  with every other contract event (`MessageReceivedV1`, `WindowClosingV1`). Fixed to use the
  already-resolved `phoneNumber.MetaPhoneNumberId` at the two call sites where it's available;
  the one exception (`UNRESOLVED_PHONE_NUMBER`) has no Meta id to report by definition and falls
  back to the GUID string, documented at that call site.
- `OutboxDispatcherService`'s instance-id used a fixed-length range (`[..64]`) on a string that is
  usually shorter than 64 characters on this machine, throwing `ArgumentOutOfRangeException` at
  host startup. Fixed with `Math.Min(100, rawInstanceId.Length)` (100 = `locked_by`'s actual
  `varchar` length, not an arbitrary number).

**Scope cuts, deliberate:** `messaging.suppression_list` (pre-dispatch do-not-send checks) exists
in V007 and is mentioned in that migration's own table comment, but issue #14's task list doesn't
call it out explicitly — left unimplemented to avoid gold-plating; flagged in the PR. Template
`category` is caller-declared in the payload rather than looked up from wa-admin-svc's template
catalog (issue #16) — avoids a cross-service dependency for Wave 1; a future issue could
cross-check it. The real per-type Meta Cloud API wire-format mapping (11 distinct message-object
shapes) is simplified to a generic envelope (`{messaging_product, to, type, payload}`) sent to a
local stub — faithful Meta wire-format mapping is real integration work that belongs with WABA
onboarding (issue #6).

Live verification evidence: idempotency (duplicate key returns the original result, live-updated
status included); ADR-005 rejection (`WINDOW_CLOSED`, 422); permanent Graph errors (131026/
131047/131049) fail fast (attempts stay at 1); a transient 500 retries with growing backoff to
exactly 5 attempts then dead-letters; rate-limit throttling is logged and delays but never drops;
crash-recovery via stale-lease reclaim proven directly (not by racing a real process kill, which
turned out to be too fast to reliably catch against the local stub — staged the exact DB state a
crash leaves behind instead: `status='dispatching'`, a `locked_at` older than the stale-lock
timeout, and watched the running dispatcher reclaim and complete it).
