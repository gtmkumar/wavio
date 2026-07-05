using wavio.SharedDataModel.Contracts;

namespace WaPlatform.IntegrationTests.Support;

/// <summary>
/// Hand-rolled <see cref="ICurrentTenant"/> fake with a mutable <see cref="TenantId"/> — CLAUDE.md
/// prefers real objects/hand-rolled fakes over mocks, and <see cref="ICurrentTenant"/> is a
/// three-property read interface with no behavior worth mocking. Registered Scoped per
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceScope"/> so
/// <c>RlsConnectionInterceptor</c> (which captures it at connection-open time) sees exactly the
/// tenant this scope was built for — the same shape as every real
/// <c>ICurrentTenant</c> implementation in this codebase (HttpContextCurrentTenant /
/// ScopedCurrentTenant), just driven by a test setting the property directly instead of a JWT
/// claim or a background-service override.
/// </summary>
public sealed class TestCurrentTenant : ICurrentTenant
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public bool BypassRls { get; set; }
}
