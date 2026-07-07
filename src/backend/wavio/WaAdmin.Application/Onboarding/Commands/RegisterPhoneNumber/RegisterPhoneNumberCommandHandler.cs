using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Dtos;
using WaAdmin.Application.Onboarding.Logic;
using wavio.SharedDataModel.Crypto;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Onboarding.Commands.RegisterPhoneNumber;

/// <summary>
/// Registers the number with the Cloud API (/register + pin), then re-polls the phone node so
/// status/name_status reflect Meta's post-registration state (PENDING → CONNECTED transition is
/// recorded in phone_number_events by <see cref="OnboardingGraphSync.ApplyPhoneInfo"/>).
/// </summary>
public sealed class RegisterPhoneNumberCommandHandler
    : ICommandHandler<RegisterPhoneNumberCommand, OnboardingPhoneDto?>
{
    private readonly IWaAdminDbContext _db;
    private readonly IWhatsAppOnboardingGraphClient _graph;
    private readonly IFieldCipher _cipher;

    public RegisterPhoneNumberCommandHandler(
        IWaAdminDbContext db, IWhatsAppOnboardingGraphClient graph, IFieldCipher cipher)
    {
        _db = db;
        _graph = graph;
        _cipher = cipher;
    }

    public async Task<OnboardingPhoneDto?> HandleAsync(
        RegisterPhoneNumberCommand command, CancellationToken cancellationToken)
    {
        if (command.Pin.Length != 6 || !command.Pin.All(char.IsAsciiDigit))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["pin"] = ["The two-step verification pin must be exactly 6 digits."],
            });
        }

        var phone = await _db.WabaPhoneNumbers
            .FirstOrDefaultAsync(p => p.Id == command.PhoneNumberId, cancellationToken);
        if (phone is null) return null;

        var (_, token) = await OnboardingGraphSync.RequireTokenAsync(
            _db, _cipher, phone.BusinessAccountId, cancellationToken);

        var result = await _graph.RegisterPhoneAsync(token, phone.MetaPhoneNumberId, command.Pin, cancellationToken);
        if (!result.Success)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["pin"] = [result.Error ?? "Meta rejected the registration."],
            });
        }

        var now = DateTimeOffset.UtcNow;
        phone.RegisteredAt = now;

        // Re-poll so status (PENDING → CONNECTED) and name_status come from Meta, not guesses.
        var info = await _graph.GetPhoneNumberAsync(token, phone.MetaPhoneNumberId, cancellationToken);
        if (info is not null)
        {
            OnboardingGraphSync.ApplyPhoneInfo(_db, phone, info, now, command.ActorId);
        }
        else
        {
            phone.UpdatedAt = now;
            phone.Version++;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var profileSet = await _db.WabaBusinessProfiles.AsNoTracking()
            .AnyAsync(bp => bp.PhoneNumberId == phone.Id, cancellationToken);
        return new OnboardingPhoneDto(
            phone.Id, phone.MetaPhoneNumberId, phone.DisplayPhoneNumber, phone.VerifiedName,
            phone.Status, phone.CodeVerificationStatus, phone.NameStatus, phone.QualityRating,
            phone.MessagingTier, phone.RegisteredAt, profileSet);
    }
}
