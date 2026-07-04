namespace wavio.SharedDataModel.Entities.Waba;

/// <summary>
/// Read-mostly mapping of waba.phone_numbers (db/migrations/V002__waba.sql) — the internal GUID
/// ↔ Meta's raw <c>meta_phone_number_id</c> string bridge every service that talks to the Graph
/// API needs (issue #14's outbox dispatcher; issue #15's tenant resolver used a raw-SQL query
/// instead since it runs before a tenant is known — this entity is for callers that already have
/// a tenant context, like the dispatcher, which resolves it via its scoped tenant override).
/// Only the columns currently consumed anywhere are mapped; the table has more (business account,
/// verified name, quality rating, etc.) not yet needed by any service.
/// </summary>
public class WabaPhoneNumber
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BusinessAccountId { get; set; }
    public string MetaPhoneNumberId { get; set; } = null!;
    public string DisplayPhoneNumber { get; set; } = null!;
    public string Status { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}
