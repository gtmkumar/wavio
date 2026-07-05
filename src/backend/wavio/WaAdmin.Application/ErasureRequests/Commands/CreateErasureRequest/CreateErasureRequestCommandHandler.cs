using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Consent.Dtos;
using wavio.SharedDataModel.Entities.Consent;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.ErasureRequests.Commands.CreateErasureRequest;

public sealed class CreateErasureRequestCommandHandler
    : ICommandHandler<CreateErasureRequestCommand, ErasureRequestDto>
{
    private static readonly HashSet<string> ValidRequestTypes = ["erasure", "export"];

    private readonly IWaAdminDbContext _db;

    public CreateErasureRequestCommandHandler(IWaAdminDbContext db) => _db = db;

    public async Task<ErasureRequestDto> HandleAsync(
        CreateErasureRequestCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.WaId))
            errors["waId"] = ["waId is required."];
        if (!ValidRequestTypes.Contains(req.RequestType))
            errors["requestType"] = [$"requestType must be one of: {string.Join(", ", ValidRequestTypes)}."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var now = DateTimeOffset.UtcNow;
        var request = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            WaId = req.WaId,
            RequestType = req.RequestType,
            Status = "pending",
            RequestedBy = req.RequestedBy,
            Reason = req.Reason,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = command.ActorId,
            UpdatedBy = command.ActorId,
            Version = 1,
        };
        _db.ErasureRequests.Add(request);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(request);
    }

    private static ErasureRequestDto ToDto(ErasureRequest r) => new(
        r.Id, r.WaId, r.RequestType, r.Status, r.Reason, r.ContentErasedAt, r.ExportRef, r.CompletedAt, r.CreatedAt);
}
