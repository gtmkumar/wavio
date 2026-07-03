namespace wavio.SharedDataModel.Entities.Kernel;

/// <summary>Hierarchical key-value settings (kernel.system_settings).
/// Has created_at, updated_at, created_by, updated_by, version, status — no deleted_at.</summary>
public class SystemSetting
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string ScopeType { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string SettingKey { get; set; } = null!;
    public string SettingValue { get; set; } = null!;
    public string DataType { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsEncrypted { get; set; }
    public bool IsReadonly { get; set; }
    public bool RequiresRestart { get; set; }
    public string? ValidationSchema { get; set; }
    public string? DefaultValue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public Guid? CreatedBy { get; set; }
    public string Status { get; set; } = null!;
}
