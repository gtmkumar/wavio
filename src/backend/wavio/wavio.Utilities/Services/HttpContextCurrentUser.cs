using System.Security.Claims;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Auth;
using Microsoft.AspNetCore.Http;

namespace wavio.Utilities.Services;

/// <summary><see cref="ICurrentUser"/> backed by HttpContext JWT claims.</summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId => ParseGuid(ClaimTypes.NameIdentifier);
    public string? UserType => Claim("user_type");
    public string? Email => Claim("email");
    public string? Phone => Claim("phone");
    public Guid? TenantId => ParseGuid("tenant_id");
    public string? ScopeType => Claim("scope_type");
    public Guid? ScopeId => ParseGuid("scope_id");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsPlatformAdmin =>
        UserType == SharedDataModel.Enums.UserType.PlatformAdmin
        || ScopeType == SharedDataModel.Enums.ScopeType.Platform;

    public bool HasPermission(string permissionCode)
    {
        var perms = Claim("permissions");
        if (string.IsNullOrEmpty(perms)) return false;
        return perms.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ScopeNode> ScopeNodes
    {
        get
        {
            var raw = Claim("scope_nodes");
            if (string.IsNullOrEmpty(raw)) return Array.Empty<ScopeNode>();
            return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Select(ScopeNode.TryParse)
                      .Where(n => n.HasValue)
                      .Select(n => n!.Value)
                      .ToArray();
        }
    }

    public bool IsWithinScope(Guid? tenantId = null)
    {
        // Platform operators are unbounded (they already bypass RLS + permission checks).
        if (IsPlatformAdmin) return true;

        // Backward-compat / rollout safety: a token with NO scope_nodes claim at all can only be a
        // genuinely pre-feature (pre-deploy) token. The real mint path (JwtTokenService.CreateAccessToken)
        // now ALWAYS emits scope_nodes for user tokens — even empty — so a membership-less principal
        // carries a PRESENT-but-empty claim that correctly DENIES here (the foreach below loops 0 nodes
        // → returns false). Since the JWT is signed the claim cannot be stripped, so an absent claim
        // reliably means "pre-feature token, not enforceable" → allow (rollout safety only).
        // A present claim is enforced normally: deny unless one of its nodes matches the target.
        if (Claim("scope_nodes") is null) return true;

        foreach (var node in ScopeNodes)
        {
            switch (node.ScopeType)
            {
                case SharedDataModel.Enums.ScopeType.Platform:
                    return true; // platform membership is an ancestor of every node
                case SharedDataModel.Enums.ScopeType.Tenant when Matches(node.ScopeId, tenantId):
                    return true;
            }
        }
        return false;

        static bool Matches(Guid? nodeId, Guid? targetId)
            => nodeId is { } n && targetId is { } t && n == t;
    }

    public Guid? TryGetTenantId()
    {
        if (_accessor.HttpContext?.Items.TryGetValue("tenant_id_override", out var overrideVal) == true
            && overrideVal is Guid overrideGuid && overrideGuid != Guid.Empty)
            return overrideGuid;

        return TenantId is { } t && t != Guid.Empty ? t : null;
    }

    public Guid RequireTenantId()
        => TryGetTenantId()
           ?? throw new UnauthorizedAccessException(
               "Tenant context required. For platform admins, pass the X-Tenant-Id header.");

    private string? Claim(string type) => Principal?.FindFirstValue(type);
    private Guid? ParseGuid(string type) => Guid.TryParse(Claim(type), out var g) ? g : null;
}
