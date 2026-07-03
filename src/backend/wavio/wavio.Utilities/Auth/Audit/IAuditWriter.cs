using System.Text.Json;
using wavio.SharedDataModel.Contracts;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Persistence;
using wavio.Utilities.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace wavio.Utilities.Auth.Audit;

/// <summary>
/// Writes a single explicit audit row for an action the <see cref="AuditSaveChangesInterceptor"/> is
/// structurally blind to: DENIED/failed attempts (no SaveChanges), reads/exports (no entity change),
/// raw-SQL / bulk ops (ExecuteUpdate/Delete bypass the change tracker), external side-effects, and
/// multi-entity SEMANTIC actions that should be one named row (e.g. pricing.pricelist.publish).
/// Stamps actor/tenant/request context identically to the interceptor.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(
        string action,
        string resourceType,
        Guid? resourceId = null,
        string? resourceDisplay = null,
        bool success = true,
        string? errorMessage = null,
        object? oldValues = null,
        object? newValues = null,
        string[]? changedFields = null,
        CancellationToken ct = default);
}

/// <summary>Default <see cref="IAuditWriter"/>. Writes through the concrete WavioDbContext
/// (AuditLogs is not exposed on the domain facade interfaces) with its own SaveChanges, and is
/// FAIL-OPEN — an audit failure is logged, never propagated to the caller's business path.</summary>
public sealed class AuditWriter : IAuditWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WavioDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly ICurrentUser _user;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditWriter> _logger;

    public AuditWriter(
        WavioDbContext db,
        ICurrentTenant tenant,
        ICurrentUser user,
        IHttpContextAccessor http,
        ILogger<AuditWriter> logger)
    {
        _db     = db;
        _tenant = tenant;
        _user   = user;
        _http   = http;
        _logger = logger;
    }

    public async Task WriteAsync(
        string action, string resourceType, Guid? resourceId = null, string? resourceDisplay = null,
        bool success = true, string? errorMessage = null,
        object? oldValues = null, object? newValues = null, string[]? changedFields = null,
        CancellationToken ct = default)
    {
        try
        {
            var log = new AuditLog
            {
                Id              = Guid.NewGuid(),
                Action          = Trunc(action, 100),
                ResourceType    = Trunc(resourceType, 50),
                ResourceId      = resourceId,
                ResourceDisplay = resourceDisplay,
                Success         = success,
                ErrorMessage    = errorMessage,
                OldValues       = oldValues is null ? null : JsonSerializer.Serialize(oldValues, Json),
                NewValues       = newValues is null ? null : JsonSerializer.Serialize(newValues, Json),
                ChangedFields   = changedFields,
            };
            AuditContext.Fill(log, _tenant, _user, _http.HttpContext);
            _db.Set<AuditLog>().Add(log);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Fail-open: an audit write must never break the caller's flow.
            _logger.LogError(ex, "Explicit audit write failed for {Action} {ResourceType}", action, resourceType);
        }
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
