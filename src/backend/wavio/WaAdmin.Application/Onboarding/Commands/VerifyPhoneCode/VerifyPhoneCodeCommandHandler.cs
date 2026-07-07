using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Dtos;
using WaAdmin.Application.Onboarding.Logic;
using wavio.SharedDataModel.Crypto;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Onboarding.Commands.VerifyPhoneCode;

public sealed class VerifyPhoneCodeCommandHandler
    : ICommandHandler<VerifyPhoneCodeCommand, OnboardingPhoneDto?>
{
    private readonly IWaAdminDbContext _db;
    private readonly IWhatsAppOnboardingGraphClient _graph;
    private readonly IFieldCipher _cipher;

    public VerifyPhoneCodeCommandHandler(
        IWaAdminDbContext db, IWhatsAppOnboardingGraphClient graph, IFieldCipher cipher)
    {
        _db = db;
        _graph = graph;
        _cipher = cipher;
    }

    public async Task<OnboardingPhoneDto?> HandleAsync(
        VerifyPhoneCodeCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Code))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = ["The verification code is required."],
            });
        }

        var phone = await _db.WabaPhoneNumbers
            .FirstOrDefaultAsync(p => p.Id == command.PhoneNumberId, cancellationToken);
        if (phone is null) return null;

        var (_, token) = await OnboardingGraphSync.RequireTokenAsync(
            _db, _cipher, phone.BusinessAccountId, cancellationToken);

        var result = await _graph.VerifyCodeAsync(token, phone.MetaPhoneNumberId, command.Code, cancellationToken);
        if (!result.Success)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = [result.Error ?? "Meta rejected the verification code."],
            });
        }

        var now = DateTimeOffset.UtcNow;
        phone.CodeVerificationStatus = "VERIFIED";
        phone.UpdatedAt = now;
        phone.Version++;
        await _db.SaveChangesAsync(cancellationToken);

        var profileSet = await _db.WabaBusinessProfiles.AsNoTracking()
            .AnyAsync(bp => bp.PhoneNumberId == phone.Id, cancellationToken);
        return new OnboardingPhoneDto(
            phone.Id, phone.MetaPhoneNumberId, phone.DisplayPhoneNumber, phone.VerifiedName,
            phone.Status, phone.CodeVerificationStatus, phone.NameStatus, phone.QualityRating,
            phone.MessagingTier, phone.RegisteredAt, profileSet);
    }
}
