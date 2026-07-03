namespace wavio.SharedDataModel.Common;

/// <summary>
/// Applied only to entities that have a deleted_at column.
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}
