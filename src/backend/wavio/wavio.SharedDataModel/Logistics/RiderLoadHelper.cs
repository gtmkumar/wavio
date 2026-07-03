using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace wavio.SharedDataModel.Logistics;

/// <summary>
/// Atomic helper for maintaining <c>logistics.riders.current_load</c>.
///
/// Call <see cref="IncrementAsync"/> when a new delivery_assignment enters an active
/// state ("assigned") and <see cref="DecrementAsync"/> when it reaches a terminal
/// state (completed / failed / cancelled).
///
/// Both methods use a guarded raw-SQL UPDATE so they are:
///   - Atomic relative to concurrent riders finishing tasks simultaneously.
///   - Guard: current_load never goes below 0 (MAX(0, current_load - 1)).
///   - Cross-BC safe: both Orders and Logistics share the same schema/DbContext,
///     so the same helper can be called from either service.
/// </summary>
public static class RiderLoadHelper
{
    /// <summary>
    /// Increments <c>current_load</c> by 1 for the given rider.
    /// Call this immediately after <c>SaveChangesAsync</c> that persists a new
    /// "assigned" delivery_assignment row.
    /// </summary>
    public static async Task IncrementAsync(
        WavioDbContext db,
        Guid riderId,
        CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE logistics.riders
               SET current_load = current_load + 1,
                   updated_at   = NOW()
             WHERE id = {riderId}
            """,
            ct);
    }

    /// <summary>
    /// Decrements <c>current_load</c> by 1 (floor 0) for the given rider.
    /// Call this after <c>SaveChangesAsync</c> that stamps completed_at /
    /// cancelled_at on a delivery_assignment row.
    /// </summary>
    public static async Task DecrementAsync(
        WavioDbContext db,
        Guid riderId,
        CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE logistics.riders
               SET current_load = GREATEST(0, current_load - 1),
                   updated_at   = NOW()
             WHERE id = {riderId}
            """,
            ct);
    }
}
