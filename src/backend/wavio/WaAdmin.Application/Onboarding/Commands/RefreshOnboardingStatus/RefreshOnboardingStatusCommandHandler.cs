using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Dtos;
using WaAdmin.Application.Onboarding.Logic;
using wavio.SharedDataModel.Crypto;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Onboarding.Commands.RefreshOnboardingStatus;

/// <summary>
/// Best-effort mirror: each Graph read that fails (token revoked, Meta hiccup) simply leaves
/// the stored value as-is — the snapshot then shows the last known state rather than erroring
/// the whole checklist.
/// </summary>
public sealed class RefreshOnboardingStatusCommandHandler
    : ICommandHandler<RefreshOnboardingStatusCommand, OnboardingStatusDto>
{
    private readonly IWaAdminDbContext _db;
    private readonly IWhatsAppOnboardingGraphClient _graph;
    private readonly IFieldCipher _cipher;

    public RefreshOnboardingStatusCommandHandler(
        IWaAdminDbContext db, IWhatsAppOnboardingGraphClient graph, IFieldCipher cipher)
    {
        _db = db;
        _graph = graph;
        _cipher = cipher;
    }

    public async Task<OnboardingStatusDto> HandleAsync(
        RefreshOnboardingStatusCommand command, CancellationToken cancellationToken)
    {
        // Same "connected account wins" ordering as OnboardingSnapshot.LoadAsync.
        var account = await _db.WabaBusinessAccounts
            .OrderByDescending(a => a.SystemUserTokenCiphertext != null)
            .ThenBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var token = _cipher.Decrypt(account?.SystemUserTokenCiphertext);

        if (account is null || string.IsNullOrEmpty(token))
        {
            // Nothing onboarded yet — the snapshot itself says "start at Connect".
            return await OnboardingSnapshot.LoadAsync(_db, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;

        var wabaInfo = await _graph.GetBusinessAccountAsync(token, account.MetaWabaId, cancellationToken);
        if (wabaInfo is not null)
        {
            account.Name = wabaInfo.Name;
            account.CurrencyCode = wabaInfo.Currency ?? account.CurrencyCode;
            account.MessageTemplateNamespace = wabaInfo.MessageTemplateNamespace ?? account.MessageTemplateNamespace;
            account.VerificationStatus = wabaInfo.BusinessVerificationStatus ?? account.VerificationStatus;
            account.UpdatedAt = now;
            account.UpdatedBy = command.ActorId;
            account.Version++;
        }

        var phones = await _db.WabaPhoneNumbers
            .Where(p => p.BusinessAccountId == account.Id)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var phone in phones)
        {
            var info = await _graph.GetPhoneNumberAsync(token, phone.MetaPhoneNumberId, cancellationToken);
            if (info is not null)
            {
                OnboardingGraphSync.ApplyPhoneInfo(_db, phone, info, now, command.ActorId);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var phoneIds = phones.Select(p => p.Id).ToArray();
        var profilePhoneIds = await _db.WabaBusinessProfiles.AsNoTracking()
            .Where(bp => phoneIds.Contains(bp.PhoneNumberId))
            .Select(bp => bp.PhoneNumberId)
            .ToListAsync(cancellationToken);
        return OnboardingSnapshot.Build(account, phones, [.. profilePhoneIds]);
    }
}
