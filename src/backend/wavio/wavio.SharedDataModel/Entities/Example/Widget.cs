using wavio.SharedDataModel.Common;

namespace wavio.SharedDataModel.Entities.Example;

/// <summary>
/// Example entity (example.widgets) demonstrating the full pattern a new feature follows:
/// Entity → EF Configuration → WavioDbContext DbSet → IOperationsDbContext surface →
/// CQRS command/query handlers (operations.Application/Example) → IEndpointGroup
/// (operations.WebApi/Endpoints/Example). Delete this whole vertical once the project
/// has its own real domain entities — it exists only to show the shape to copy.
/// </summary>
public class Widget : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
