using System.Text.Json;
using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Consent.Dtos;
using wavio.SharedDataModel.Entities.Consent;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Consent.Commands.RecordOptIn;

/// <summary>
/// Writes the opt-in evidence row. Manual guard clauses, not a FluentValidation validator — this
/// codebase's CQRS ValidationBehavior is registered but never wired into the active Dispatcher
/// (see WaBilling's UpsertRateCardCommandHandler / issue-19 agent memory,
/// cqrs-validation-pipeline-is-dead-code), so an AbstractValidator here would silently never run.
/// </summary>
public sealed class RecordOptInCommandHandler : ICommandHandler<RecordOptInCommand, OptInEventDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> ValidPurposes = ["transactional", "marketing", "service"];
    private static readonly HashSet<string> ValidCaptureChannels =
        ["web_form", "qr", "in_chat", "in_person", "api", "import"];

    private readonly IWaAdminDbContext _db;

    public RecordOptInCommandHandler(IWaAdminDbContext db) => _db = db;

    public async Task<OptInEventDto> HandleAsync(RecordOptInCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(req.WaId))
            errors["waId"] = ["waId is required."];
        if (!ValidPurposes.Contains(req.Purpose))
            errors["purpose"] = [$"purpose must be one of: {string.Join(", ", ValidPurposes)}."];
        if (!ValidCaptureChannels.Contains(req.CaptureChannel))
            errors["captureChannel"] = [$"captureChannel must be one of: {string.Join(", ", ValidCaptureChannels)}."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var now = DateTimeOffset.UtcNow;

        // Behalf-of consent (spec §4.10): fold the explicit typed request fields into the
        // evidence jsonb rather than a schema change — see RecordOptInRequest's doc comment.
        string? evidenceJson = null;
        if (!string.IsNullOrWhiteSpace(req.OnBehalfOfWaId)
            || !string.IsNullOrWhiteSpace(req.OnBehalfOfName)
            || !string.IsNullOrWhiteSpace(req.EvidenceProofRef))
        {
            evidenceJson = JsonSerializer.Serialize(new
            {
                onBehalfOf = (!string.IsNullOrWhiteSpace(req.OnBehalfOfWaId) || !string.IsNullOrWhiteSpace(req.OnBehalfOfName))
                    ? new { waId = req.OnBehalfOfWaId, name = req.OnBehalfOfName }
                    : null,
                proofRef = req.EvidenceProofRef,
            }, JsonOptions);
        }

        var optIn = new OptInEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            WaId = req.WaId,
            Purpose = req.Purpose,
            CaptureChannel = req.CaptureChannel,
            Evidence = evidenceJson,
            EvidenceWamid = req.EvidenceWamid,
            Actor = req.Actor,
            SourceIp = command.SourceIp,
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = command.ActorId,
        };
        _db.OptInEvents.Add(optIn);
        await _db.SaveChangesAsync(cancellationToken);

        return new OptInEventDto(
            optIn.Id, optIn.WaId, optIn.Purpose, optIn.CaptureChannel,
            optIn.EvidenceWamid, optIn.Actor, optIn.OccurredAt);
    }
}
