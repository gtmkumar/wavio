namespace wavio.SharedDataModel.Common;

/// <summary>
/// Applied only to entities that have ALL of: created_at, updated_at, created_by, updated_by, version.
/// Match each table precisely — do not apply to tables missing any of these columns.
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    Guid? UpdatedBy { get; set; }
    int Version { get; set; }
}
