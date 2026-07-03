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

    public Guid? TenantId => GetGuid("tenant_id");
    public Guid? UserId => GetGuid(ClaimTypes.NameIdentifier);
    public bool BypassRls => _accessor.HttpContext?.Items["bypass_rls"] is true;

    private Guid? GetGuid(string claimType)
    {
        var value = _accessor.HttpContext?.User?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var g) ? g : null;
    }
}
