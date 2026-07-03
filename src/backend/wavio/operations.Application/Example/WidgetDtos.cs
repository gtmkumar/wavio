namespace operations.Application.Example;

public sealed record WidgetDto(
    Guid Id, Guid TenantId, string Name, string? Description, string Status, DateTimeOffset CreatedAt);

public sealed record CreateWidgetRequest(string Name, string? Description);

public sealed record UpdateWidgetRequest(string Name, string? Description, string Status);
