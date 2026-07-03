using System.Security.Claims;
using wavio.SharedDataModel.Contracts;
using wavio.Utilities.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace wavio.Utilities.Middlewares;

/// <summary>
/// Runs after authentication. Reads tenant_id from JWT claims and applies the X-Tenant-Id
/// override for platform admins. Sets HttpContext.Items["bypass_rls"] = true for platform
/// admins so ICurrentTenant.BypassRls works, and HttpContext.Items["tenant_id_override"] so
/// ICurrentUser.RequireTenantId() can resolve a tenant.
/// Also enforces live token revocation via perm_version when Auth:EnforceTokenVersion is on.
/// Cross-cutting: every WebApi host (core / operations / commerce) wires this between
/// UseAuthentication() and UseAuthorization().
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enforceTokenVersion;

    public TenantResolutionMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _enforceTokenVersion = config.GetValue<bool>("Auth:EnforceTokenVersion");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userType = context.User.FindFirstValue("user_type");
            bool isPlatformAdmin = userType == wavio.SharedDataModel.Enums.UserType.PlatformAdmin;

            // Platform admins get RLS bypass so they can read across tenants.
            if (isPlatformAdmin)
            {
                context.Items["bypass_rls"] = true;

                // Optional: allow explicit X-Tenant-Id override to narrow to a specific tenant
                if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader)
                    && Guid.TryParse(tenantHeader, out var tenantOverride))
                {
                    context.Items["tenant_id_override"] = tenantOverride;
                }
            }

            // Live revocation: reject a system-user token whose stamped perm_version is stale,
            // forcing the client to silently refresh and pick up the new permissions. Fail OPEN
            // (never block on a missing claim / unknown user / lookup error) so a guard fault
            // can't lock anyone out. Gated by Auth:EnforceTokenVersion (default off).
            if (_enforceTokenVersion
                && context.User.FindFirstValue("token_use") == TokenClaims.TokenUseValue
                && int.TryParse(context.User.FindFirstValue(TokenClaims.PermVersionClaim), out var tokenVer)
                && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var store = context.RequestServices.GetService<ITokenVersionStore>();
                if (store is not null)
                {
                    var current = await store.GetPermVersionAsync(userId, context.RequestAborted);
                    if (current.HasValue && current.Value != tokenVer)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers.WWWAuthenticate =
                            "Bearer error=\"invalid_token\", error_description=\"stale permissions\"";
                        return; // short-circuit: do not invoke the endpoint
                    }
                }
            }
        }

        await _next(context);
    }
}
