using System.Text.Json;
using core.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Kernel;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Common;

/// <summary>
/// Helpers for reading and upserting tenant-scoped rows in <c>kernel.system_settings</c>. The RLS
/// connection interceptor scopes every query to the request's tenant, so lookups are by
/// (category, key) only. Add a Load*Async for each settings category the project needs
/// (mirroring LoadProvisioningModeAsync/LoadAdminBaseUrlAsync).
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The tenant these settings belong to — the caller's tenant, or the only tenant for a platform admin.</summary>
    public static async Task<Guid?> ResolveTenantIdAsync(wavio.Utilities.Services.ICurrentUser user, ICoreDbContext db, CancellationToken ct)
    {
        if (user.TenantId is Guid t) return t;
        return await db.Tenants.AsNoTracking().OrderBy(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    }

    public static Task<SystemSetting?> FindAsync(ICoreDbContext db, Guid? tenantId, string category, string key, CancellationToken ct)
    {
        var q = db.SystemSettings.Where(s => s.Category == category && s.SettingKey == key && s.Status == "active");
        if (tenantId.HasValue) q = q.Where(s => s.TenantId == tenantId);
        return q.OrderBy(s => s.TenantId == null).FirstOrDefaultAsync(ct);
    }

    public static async Task<string> LoadProvisioningModeAsync(ICoreDbContext db, Guid? tenantId, CancellationToken ct)
    {
        var row = await FindAsync(db, tenantId, "provisioning", "invite", ct);
        if (row is null) return "admin_activate";
        try
        {
            using var doc = JsonDocument.Parse(row.SettingValue);
            return doc.RootElement.TryGetProperty("mode", out var m) ? m.GetString() ?? "admin_activate" : "admin_activate";
        }
        catch (JsonException) { return "admin_activate"; }
    }

    public static async Task<string> LoadAdminBaseUrlAsync(ICoreDbContext db, Guid? tenantId, CancellationToken ct)
    {
        var row = await FindAsync(db, tenantId, "app", "urls", ct);
        const string fallback = "http://localhost:5173";
        if (row is null) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(row.SettingValue);
            return doc.RootElement.TryGetProperty("adminBaseUrl", out var u) ? (u.GetString() ?? fallback) : fallback;
        }
        catch (JsonException) { return fallback; }
    }

    // ── Masking helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns "••••" + the last 4 characters of <paramref name="plaintext"/>,
    /// or null when the value is null/empty.
    /// </summary>
    public static string? MaskSecret(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        return plaintext.Length >= 4
            ? $"••••{plaintext[^4..]}"
            : "••••";
    }

    /// <summary>Upsert a setting's JSON value, creating the row if it does not exist yet.</summary>
    public static async Task UpsertAsync(
        ICoreDbContext db, Guid? tenantId, string category, string key,
        object value, bool isEncrypted, Guid? actorId, CancellationToken ct)
    {
        var row = await FindAsync(db, tenantId, category, key, ct);
        var jsonValue = JsonSerializer.Serialize(value, Json);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.NewGuid(), ScopeType = tenantId.HasValue ? "tenant" : "platform", TenantId = tenantId,
                Category = category, SettingKey = key, SettingValue = jsonValue, DataType = "object",
                IsEncrypted = isEncrypted, Status = "active", Version = 1,
                CreatedAt = now, UpdatedAt = now, CreatedBy = actorId, UpdatedBy = actorId,
            });
        }
        else
        {
            row.SettingValue = jsonValue;
            row.IsEncrypted = isEncrypted;
            row.UpdatedAt = now;
            row.UpdatedBy = actorId;
            row.Version++;
        }
        await db.SaveChangesAsync(ct);
    }
}
