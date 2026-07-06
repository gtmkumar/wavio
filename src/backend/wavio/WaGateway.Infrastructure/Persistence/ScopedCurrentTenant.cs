using System.Security.Claims;
using wavio.SharedDataModel.Contracts;
using Microsoft.AspNetCore.Http;

namespace WaGateway.Infrastructure.Persistence;

/// <summary>
/// <see cref="ICurrentTenant"/> that serves BOTH HTTP requests and the background outbox
/// dispatcher from the same registration (replaces the shared <c>HttpContextCurrentTenant</c> in
/// Program.cs via <c>Replace(...)</c>) — same pattern as wa-admin-svc's <c>ScopedCurrentTenant</c>
/// (issue #16), reimplemented locally rather than shared across services.
///
/// HTTP requests: falls through to the JWT <c>tenant_id</c> claim exactly like
/// <c>HttpContextCurrentTenant</c> — unchanged behavior for the send endpoint.
///
/// Background dispatcher: <c>HttpContextCurrentTenant</c> alone cannot work outside a request (no
/// HttpContext -&gt; empty GUC -&gt; RLS returns/accepts zero rows for every tenant-scoped query —
/// discovered live during issue #15 and documented in
/// .claude/agent-memory/dotnet-backend-developer/rls-background-service-guc-gotcha.md).
/// <c>OutboxDispatcherService</c> creates one DI scope per outbox entry and sets
/// <see cref="OverrideTenantId"/> to that entry's TenantId before resolving any DbContext-backed
/// service, so RLS scopes correctly to that one tenant for the status update on
/// <c>outbound_messages</c> (which IS RLS-scoped, unlike <c>outbound_outbox</c> itself). Never
/// used to bypass RLS across tenants — the dispatcher still only ever touches one tenant's row
/// per scope.
/// </summary>
public sealed class ScopedCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _accessor;

    public ScopedCurrentTenant(IHttpContextAccessor accessor) => _accessor = accessor;

    /// <summary>Set by the background dispatcher before any tenant-scoped DB access in this
    /// scope. Left null for ordinary HTTP requests, where JWT claims are used instead.</summary>
    public Guid? OverrideTenantId { get; set; }

    /// <summary>HTTP precedence mirrors <c>HttpContextCurrentTenant</c>: the platform-admin
    /// X-Tenant-Id override (Items["tenant_id_override"], only set by TenantResolutionMiddleware
    /// for user_type=platform_admin) wins over the JWT claim so RLS scopes to the acted-on
    /// tenant — without it a platform admin has no tenant claim and every FORCE-RLS query
    /// returns nothing (the parked platform-admin-write-rls-gap).</summary>
    public Guid? TenantId =>
        OverrideTenantId
        ?? (_accessor.HttpContext?.Items["tenant_id_override"] is Guid headerOverride
            ? headerOverride
            : GetGuid("tenant_id"));

    public Guid? UserId => GetGuid(ClaimTypes.NameIdentifier);

    // Background dispatcher never bypasses RLS — it operates strictly within the one tenant it set.
    public bool BypassRls => OverrideTenantId is null && _accessor.HttpContext?.Items["bypass_rls"] is true;

    private Guid? GetGuid(string claimType)
    {
        var value = _accessor.HttpContext?.User?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var g) ? g : null;
    }
}
