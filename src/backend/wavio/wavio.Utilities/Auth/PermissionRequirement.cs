using Microsoft.AspNetCore.Authorization;

namespace wavio.Utilities.Auth;

/// <summary>Authorization requirement: caller must have the specified permission code in their JWT.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string PermissionCode { get; }
    public PermissionRequirement(string permissionCode) => PermissionCode = permissionCode;
}
