using System.Security.Claims;
using wavio.SharedDataModel.Contracts;
using Microsoft.AspNetCore.Http;

namespace wavio.Utilities.Services;

/// <summary>
/// <see cref="ICurrentTenant"/> backed by HttpContext JWT claims, consumed by the shared
/// RLS connection interceptor. Populated by TenantResolutionMiddleware after authentication;
/// platform admins may set <c>BypassRls=true</c> via <c>HttpContext.Items["bypass_rls"]</c>.
///
/// Cross-cutting: shared by every bounded-context host (Core, Operations, …) — one tenant
/// adapter serves the whole service. Register via <c>services.AddCurrentTenant()</c>.
/// </summary>
public sealed class HttpContextCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentTenant(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    /// <summary>
    /// The platform-admin X-Tenant-Id override (stored by TenantResolutionMiddleware, which
    /// only sets it for user_type=platform_admin) takes precedence over the JWT claim so the
    /// RLS GUC scopes reads AND writes to the acted-on tenant. Without it a platform admin has
    /// no tenant claim, app.tenant_id stays empty, and every FORCE-RLS table returns nothing —
    /// the gap previously parked as platform-admin-write-rls-gap.
    /// </summary>
    public Guid? TenantId =>
        _accessor.HttpContext?.Items["tenant_id_override"] is Guid overrideId
            ? overrideId
            : GetGuid("tenant_id");
    public Guid? UserId => GetGuid(ClaimTypes.NameIdentifier);
    public bool BypassRls => _accessor.HttpContext?.Items["bypass_rls"] is true;

    private Guid? GetGuid(string claimType)
    {
        var value = _accessor.HttpContext?.User?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var g) ? g : null;
    }
}
