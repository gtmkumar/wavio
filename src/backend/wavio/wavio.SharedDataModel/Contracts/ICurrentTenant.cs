namespace wavio.SharedDataModel.Contracts;

/// <summary>
/// Provides the current tenant context for Row-Level Security configuration.
/// Implementations live in the individual service projects, not in this library.
/// </summary>
public interface ICurrentTenant
{
    Guid? TenantId { get; }
    Guid? UserId { get; }

    /// <summary>When true the RLS interceptor sets app.bypass_rls = 'true'.</summary>
    bool BypassRls { get; }
}
