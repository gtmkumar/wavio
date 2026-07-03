using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace wavio.Utilities.Auth.Audit;

/// <summary>
/// Registers the RBAC §8/§12 audit trail. Call from every host's Program.cs AFTER AddSharedDataModel
/// + AddCurrentUser. Both services are Scoped (per-request) so each carries its own tenant/actor
/// snapshot. SharedDataModel's AddDbContext enumerates <see cref="ISaveChangesInterceptor"/> from the
/// request scope and attaches this interceptor — so the wiring needs no SharedDataModel→Utilities ref.
/// </summary>
public static class AuditTrailServiceCollectionExtensions
{
    public static IServiceCollection AddAuditTrail(this IServiceCollection services)
    {
        services.AddScoped<ISaveChangesInterceptor, AuditSaveChangesInterceptor>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        return services;
    }
}
