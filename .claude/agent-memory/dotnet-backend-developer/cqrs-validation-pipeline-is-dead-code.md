---
name: cqrs-validation-pipeline-is-dead-code
description: FluentValidation validators registered via AddValidatorsFromAssembly never actually run — the active IDispatcher doesn't execute the pipeline behaviors that would invoke them
metadata:
  type: project
---

`wavio.Utilities/CQRS` ships TWO dispatcher implementations:
`Dispatcher.Dispatcher` (registered by `AddCustomCQRS` as the actual `IDispatcher`) resolves the
handler and calls it directly — no pipeline. `Dispatcher.CommandDispatcher` (unused, not
registered anywhere) is the one that actually walks the `IPipelineBehavior<,>` chain
(`BehaviorRegistrar.RegisterBehaviors`, which is also never called). Confirmed by grepping the
whole `src/backend/wavio` tree for `RegisterBehaviors`/`CommandDispatcher`/`QueryDispatcher`
usage outside their own definition files — zero hits (2026-07-04, checked on
`feature/16-template-lifecycle`, same on `feature/13-ingest-webhooks`).

**Consequence**: `AbstractValidator<TCommand>` classes registered via
`services.AddValidatorsFromAssembly(...)` are constructed by DI (harmless) but **never invoked** —
`ValidationBehavior<,>` never runs because nothing puts it in the actual call path. Every existing
handler that needs input validation (core.Application's `CreateUserCommandHandler`, and now
WaAdmin.Application's Create/UpdateTemplateCommandHandler) does it with manual guard clauses +
`throw new Wavio.Utilities.CQRS... ValidationException(...)` / `wavio.Utilities.Exceptions
.ValidationException` directly inside `HandleAsync`, not with a separate validator class.

**How to apply**: don't write an `AbstractValidator<TCommand>` for a new CQRS command expecting it
to run automatically — it won't. Either add manual guard clauses in the handler (the established
pattern) or, if you actually want the pipeline to fire, that's a separate infra fix (wire
`RegisterBehaviors` into DI and switch `AddCustomCQRS` to register `CommandDispatcher`/
`QueryDispatcher` instead of the no-op `Dispatcher`) — flag that as its own decision with whoever
owns `wavio.Utilities`, don't silently "fix" it as a side effect of an unrelated feature.
