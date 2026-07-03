using System.Globalization;
using core.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl;

/// <summary>
/// Module taxonomy for the Access-Control permission matrix, derived directly from the
/// distinct module prefixes present in the permission catalog (<c>identity_access.permissions</c>) —
/// e.g. permission code <c>users.create</c> yields the module row <c>users</c>. No separate
/// module-catalog table is required; add a hand-curated one (label overrides, custom ordering,
/// grouping several raw modules under one UI row) if the project needs it later.
/// </summary>
public sealed class ModuleMatrix
{
    public IReadOnlyList<(string Key, string Label)> Rows { get; }
    private static readonly List<string> SettingsFallback = ["settings"];

    private ModuleMatrix(IReadOnlyList<(string, string)> rows)
    {
        Rows = rows;
    }

    public static async Task<ModuleMatrix> LoadAsync(ICoreDbContext db, CancellationToken ct)
    {
        var codes = await db.Permissions.AsNoTracking().Select(p => p.Code).ToListAsync(ct);

        var rows = codes
            .Select(PermissionMatrix.Module)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .Select(key => (key, Label(key)))
            .ToList();

        return new ModuleMatrix(rows);
    }

    private static string Label(string key) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace('_', ' '));

    /// <summary>The matrix cells ("module:action") a permission satisfies.</summary>
    public IEnumerable<string> CellsFor(string module, string action)
    {
        var uiKeys = Rows.Any(r => r.Key.Equals(module, StringComparison.OrdinalIgnoreCase))
            ? new List<string> { module }
            : SettingsFallback;
        foreach (var ui in uiKeys)
            foreach (var col in PermissionMatrix.Columns(action))
                yield return $"{ui}:{col}";
    }
}
