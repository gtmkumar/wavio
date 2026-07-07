using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Dtos;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Onboarding.Logic;

/// <summary>
/// Builds the wizard's <see cref="OnboardingStatusDto"/> from DB state — the one place the
/// step-4 checklist semantics live, shared by the status query, refresh, and embedded-signup
/// handlers so every path returns an identical snapshot shape.
/// </summary>
public static class OnboardingSnapshot
{
    /// <summary>Loads the tenant's onboarding state (RLS scopes the tenant; the wizard concept
    /// is one WABA per tenant). A CONNECTED account (token stored) wins over token-less rows —
    /// dev fixtures and half-finished imports must not shadow the actually-onboarded WABA.</summary>
    public static async Task<OnboardingStatusDto> LoadAsync(IWaAdminDbContext db, CancellationToken cancellationToken)
    {
        var account = await db.WabaBusinessAccounts.AsNoTracking()
            .OrderByDescending(a => a.SystemUserTokenCiphertext != null)
            .ThenBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (account is null) return Build(null, [], []);

        var phones = await db.WabaPhoneNumbers.AsNoTracking()
            .Where(p => p.BusinessAccountId == account.Id)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var phoneIds = phones.Select(p => p.Id).ToArray();
        var profilePhoneIds = await db.WabaBusinessProfiles.AsNoTracking()
            .Where(bp => phoneIds.Contains(bp.PhoneNumberId))
            .Select(bp => bp.PhoneNumberId)
            .ToListAsync(cancellationToken);

        return Build(account, phones, [.. profilePhoneIds]);
    }

    public static OnboardingStatusDto Build(
        WabaBusinessAccount? account,
        IReadOnlyList<WabaPhoneNumber> phones,
        HashSet<Guid> profilePhoneIds)
    {
        var connected = account is not null && !string.IsNullOrEmpty(account.SystemUserTokenCiphertext);

        var accountDto = account is null
            ? null
            : new OnboardingBusinessAccountDto(
                account.Id, account.MetaWabaId, account.Name, account.CurrencyCode,
                account.VerificationStatus, account.WebhooksSubscribedAt,
                HasToken: !string.IsNullOrEmpty(account.SystemUserTokenCiphertext));

        var phoneDtos = phones.Select(p => new OnboardingPhoneDto(
            p.Id, p.MetaPhoneNumberId, p.DisplayPhoneNumber, p.VerifiedName, p.Status,
            p.CodeVerificationStatus, p.NameStatus, p.QualityRating, p.MessagingTier,
            p.RegisteredAt, ProfileSet: profilePhoneIds.Contains(p.Id))).ToList();

        return new OnboardingStatusDto(connected, accountDto, phoneDtos, BuildChecks(connected, account, phoneDtos));
    }

    // The wizard tracks the FIRST phone number — the concept slice onboards one number
    // (franchise multi-number routing is explicitly out of scope, see the plan doc).
    private static List<OnboardingCheckDto> BuildChecks(
        bool connected, WabaBusinessAccount? account, IReadOnlyList<OnboardingPhoneDto> phones)
    {
        var phone = phones.Count > 0 ? phones[0] : null;

        var checks = new List<OnboardingCheckDto>
        {
            new("connected", connected ? "done" : "todo", account?.MetaWabaId),
            new("webhooks", account?.WebhooksSubscribedAt is not null ? "done" : "todo", null),
            new("number_verified",
                phone?.CodeVerificationStatus == "VERIFIED" ? "done" : "todo",
                phone?.CodeVerificationStatus),
            new("number_registered", phone?.RegisteredAt is not null ? "done" : "todo", phone?.Status),
            new("profile", phone?.ProfileSet == true ? "done" : "todo", null),
            new("name_review", phone?.NameStatus switch
            {
                "APPROVED" or "AVAILABLE_WITHOUT_REVIEW" => "done",
                "PENDING_REVIEW" => "waiting",
                "DECLINED" or "EXPIRED" => "attention",
                _ => "todo",
            }, phone?.NameStatus),
            new("business_verification", account?.VerificationStatus switch
            {
                "verified" => "done",
                "pending" => "waiting",
                null => "todo",
                _ => "attention", // e.g. not_verified / failed — Meta wants something from the business
            }, account?.VerificationStatus),
            new("quality", phone?.QualityRating switch
            {
                "GREEN" => "done",
                "YELLOW" or "RED" => "attention",
                _ => phone?.RegisteredAt is null ? "todo" : "waiting",
            }, phone is null ? null : $"{phone.QualityRating ?? "UNKNOWN"} / {phone.MessagingTier ?? "no tier yet"}"),
        };

        return checks;
    }
}
