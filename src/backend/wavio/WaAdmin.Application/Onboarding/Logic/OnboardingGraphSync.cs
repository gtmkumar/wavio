using WaAdmin.Application.Common.Interfaces;
using wavio.SharedDataModel.Crypto;
using wavio.SharedDataModel.Entities.Waba;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Onboarding.Logic;

/// <summary>
/// Shared plumbing for onboarding handlers: resolving the decrypted per-WABA business token and
/// mirroring a Graph phone-status read into the tracked <see cref="WabaPhoneNumber"/> row
/// (including the V002-mandated phone_number_events audit row on a status transition).
/// </summary>
public static class OnboardingGraphSync
{
    /// <summary>Decrypts the business token for the phone's owning WABA, or throws a 422 telling
    /// the caller to run the Connect step first. The plaintext token must never outlive the
    /// Graph call it authorizes — callers pass it straight through and drop it.</summary>
    public static async Task<(WabaBusinessAccount Account, string Token)> RequireTokenAsync(
        IWaAdminDbContext db, IFieldCipher cipher, Guid businessAccountId, CancellationToken cancellationToken)
    {
        var account = await db.WabaBusinessAccounts
            .FirstOrDefaultAsync(a => a.Id == businessAccountId, cancellationToken);
        var token = cipher.Decrypt(account?.SystemUserTokenCiphertext);

        if (account is null || string.IsNullOrEmpty(token))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["businessAccount"] = ["This WhatsApp business account is not connected yet — complete the Connect step first."],
            });
        }

        return (account, token);
    }

    /// <summary>Mirrors a Graph phone read into the tracked entity. A status transition also
    /// appends a waba.phone_number_events row (V002's state machine is app-enforced and logged).</summary>
    public static void ApplyPhoneInfo(
        IWaAdminDbContext db, WabaPhoneNumber phone, GraphPhoneInfo info, DateTimeOffset now, Guid? actorId)
    {
        phone.VerifiedName = info.VerifiedName ?? phone.VerifiedName;
        phone.CodeVerificationStatus = info.CodeVerificationStatus ?? phone.CodeVerificationStatus;
        phone.NameStatus = info.NameStatus ?? phone.NameStatus;
        phone.QualityRating = info.QualityRating ?? phone.QualityRating;
        phone.MessagingTier = info.MessagingTier ?? phone.MessagingTier;

        if (!string.IsNullOrEmpty(info.Status) && info.Status != phone.Status)
        {
            db.WabaPhoneNumberEvents.Add(new WabaPhoneNumberEvent
            {
                Id = Guid.NewGuid(),
                TenantId = phone.TenantId,
                PhoneNumberId = phone.Id,
                EventType = "status_changed",
                OldStatus = phone.Status,
                NewStatus = info.Status,
                OccurredAt = now,
                CreatedAt = now,
                CreatedBy = actorId,
            });
            phone.Status = info.Status;
        }

        phone.UpdatedAt = now;
        phone.Version++;
    }
}
