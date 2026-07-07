using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Dtos;
using WaAdmin.Application.Onboarding.Logic;
using wavio.SharedDataModel.Crypto;
using wavio.SharedDataModel.Entities.Waba;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Onboarding.Commands.CompleteEmbeddedSignup;

/// <summary>
/// Exchange → discover → upsert → subscribe. The business token is encrypted with
/// <see cref="IFieldCipher"/> the moment it arrives and only the ciphertext is persisted
/// (waba.business_accounts.system_user_token_ciphertext, spec §5) — the plaintext never leaves
/// this handler and is never logged. All writes land in one SaveChanges so a partial signup
/// cannot persist. Manual guard clauses, not FluentValidation — same rationale as
/// RecordOptInCommandHandler.
/// </summary>
public sealed class CompleteEmbeddedSignupCommandHandler
    : ICommandHandler<CompleteEmbeddedSignupCommand, OnboardingStatusDto>
{
    private readonly IWaAdminDbContext _db;
    private readonly IWhatsAppOnboardingGraphClient _graph;
    private readonly IFieldCipher _cipher;

    public CompleteEmbeddedSignupCommandHandler(
        IWaAdminDbContext db, IWhatsAppOnboardingGraphClient graph, IFieldCipher cipher)
    {
        _db = db;
        _graph = graph;
        _cipher = cipher;
    }

    public async Task<OnboardingStatusDto> HandleAsync(
        CompleteEmbeddedSignupCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;
        if (string.IsNullOrWhiteSpace(req.Code))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = ["The Embedded Signup authorization code is required."],
            });
        }

        var tokenResult = await _graph.ExchangeCodeAsync(req.Code, cancellationToken);
        if (!tokenResult.Success || string.IsNullOrEmpty(tokenResult.AccessToken))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = [tokenResult.Error ?? "Meta rejected the authorization code."],
            });
        }
        var accessToken = tokenResult.AccessToken;

        // Prefer the popup's sessionInfo WABA id; fall back to token introspection (stub mode
        // and any real-world case where the sessionInfo message was lost).
        var metaWabaId = req.WabaId;
        if (string.IsNullOrWhiteSpace(metaWabaId))
        {
            var granted = await _graph.GetGrantedWabaIdsAsync(accessToken, cancellationToken);
            metaWabaId = granted.Count > 0 ? granted[0] : null;
        }
        if (string.IsNullOrWhiteSpace(metaWabaId))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = ["The token grants access to no WhatsApp Business Account."],
            });
        }

        var wabaInfo = await _graph.GetBusinessAccountAsync(accessToken, metaWabaId, cancellationToken)
            ?? throw new ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = [$"WhatsApp Business Account {metaWabaId} could not be read with the granted token."],
            });
        var phoneInfos = await _graph.GetPhoneNumbersAsync(accessToken, metaWabaId, cancellationToken);

        // Subscribe webhooks before persisting so webhooks_subscribed_at is honest evidence.
        var subscribe = await _graph.SubscribeAppAsync(accessToken, metaWabaId, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // Upsert by meta_waba_id. RLS scopes the lookup to this tenant; the column's global
        // unique constraint means a WABA already onboarded by ANOTHER tenant surfaces as a
        // unique violation (500) rather than silent cross-tenant reassignment — acceptable for
        // the concept slice and impossible to hit with per-tenant simulated codes.
        var account = await _db.WabaBusinessAccounts
            .FirstOrDefaultAsync(a => a.MetaWabaId == metaWabaId, cancellationToken);
        if (account is null)
        {
            account = new WabaBusinessAccount
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                MetaWabaId = metaWabaId,
                Status = "active",
                CreatedAt = now,
                CreatedBy = command.ActorId,
                Version = 0, // bumped to 1 by the shared update below
            };
            _db.WabaBusinessAccounts.Add(account);
        }

        account.Name = wabaInfo.Name;
        account.CurrencyCode = wabaInfo.Currency;
        account.MessageTemplateNamespace = wabaInfo.MessageTemplateNamespace;
        account.VerificationStatus = wabaInfo.BusinessVerificationStatus;
        account.SystemUserTokenCiphertext = _cipher.Encrypt(accessToken);
        account.TokenKeyRef = "master:v1"; // the IFieldCipher master key; rotation bookkeeping, spec §5
        if (subscribe.Success) account.WebhooksSubscribedAt = now;
        account.UpdatedAt = now;
        account.UpdatedBy = command.ActorId;
        account.Version++;

        var existingPhones = await _db.WabaPhoneNumbers
            .Where(p => p.BusinessAccountId == account.Id)
            .ToListAsync(cancellationToken);

        foreach (var info in phoneInfos)
        {
            var phone = existingPhones.FirstOrDefault(p => p.MetaPhoneNumberId == info.MetaPhoneNumberId);
            if (phone is null)
            {
                phone = new WabaPhoneNumber
                {
                    Id = Guid.NewGuid(),
                    TenantId = command.TenantId,
                    BusinessAccountId = account.Id,
                    MetaPhoneNumberId = info.MetaPhoneNumberId,
                    DisplayPhoneNumber = info.DisplayPhoneNumber,
                    Status = "PENDING",
                    CreatedAt = now,
                    Version = 0,
                };
                _db.WabaPhoneNumbers.Add(phone);
                existingPhones.Add(phone);
            }
            phone.DisplayPhoneNumber = info.DisplayPhoneNumber;
            OnboardingGraphSync.ApplyPhoneInfo(_db, phone, info, now, command.ActorId);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var profilePhoneIds = await _db.WabaBusinessProfiles.AsNoTracking()
            .Select(bp => bp.PhoneNumberId)
            .ToListAsync(cancellationToken);
        return OnboardingSnapshot.Build(account, existingPhones, [.. profilePhoneIds]);
    }
}
