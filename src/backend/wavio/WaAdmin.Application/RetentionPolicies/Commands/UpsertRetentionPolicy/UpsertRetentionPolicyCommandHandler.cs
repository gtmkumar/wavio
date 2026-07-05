using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Consent.Dtos;
using wavio.SharedDataModel.Entities.Consent;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.RetentionPolicies.Commands.UpsertRetentionPolicy;

public sealed class UpsertRetentionPolicyCommandHandler
    : ICommandHandler<UpsertRetentionPolicyCommand, RetentionPolicyDto>
{
    private static readonly HashSet<string> ValidDataClasses =
        ["message_content", "metadata", "cost_ledger", "consent_evidence", "raw_webhook"];

    private readonly IWaAdminDbContext _db;

    public UpsertRetentionPolicyCommandHandler(IWaAdminDbContext db) => _db = db;

    public async Task<RetentionPolicyDto> HandleAsync(
        UpsertRetentionPolicyCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;
        var errors = new Dictionary<string, string[]>();
        if (!ValidDataClasses.Contains(req.DataClass))
            errors["dataClass"] = [$"dataClass must be one of: {string.Join(", ", ValidDataClasses)}."];
        if (req.RetentionDays <= 0)
            errors["retentionDays"] = ["retentionDays must be greater than 0."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var now = DateTimeOffset.UtcNow;
        var policy = await _db.RetentionPolicies.FirstOrDefaultAsync(
            p => p.TenantId == command.TenantId && p.DataClass == req.DataClass, cancellationToken);

        if (policy is null)
        {
            policy = new RetentionPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                DataClass = req.DataClass,
                CreatedAt = now,
                CreatedBy = command.ActorId,
                Version = 1,
            };
            _db.RetentionPolicies.Add(policy);
        }
        else
        {
            policy.Version += 1;
        }

        policy.RetentionDays = req.RetentionDays;
        policy.Basis = req.Basis;
        policy.Enabled = req.Enabled;
        policy.UpdatedAt = now;
        policy.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);

        return new RetentionPolicyDto(
            policy.Id, policy.TenantId, policy.DataClass, policy.RetentionDays, policy.Basis, policy.Enabled, policy.UpdatedAt);
    }
}
