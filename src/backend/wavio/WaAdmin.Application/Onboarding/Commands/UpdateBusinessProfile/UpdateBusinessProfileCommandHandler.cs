using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Dtos;
using WaAdmin.Application.Onboarding.Logic;
using wavio.SharedDataModel.Crypto;
using wavio.SharedDataModel.Entities.Waba;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Onboarding.Commands.UpdateBusinessProfile;

/// <summary>
/// Meta first, DB second: the Graph update must succeed before the local mirror row is touched,
/// so waba.business_profiles never claims a profile Meta doesn't have. Length limits mirror
/// V002's column constraints (which themselves mirror Meta's documented caps).
/// </summary>
public sealed class UpdateBusinessProfileCommandHandler
    : ICommandHandler<UpdateBusinessProfileCommand, BusinessProfileDto?>
{
    private readonly IWaAdminDbContext _db;
    private readonly IWhatsAppOnboardingGraphClient _graph;
    private readonly IFieldCipher _cipher;

    public UpdateBusinessProfileCommandHandler(
        IWaAdminDbContext db, IWhatsAppOnboardingGraphClient graph, IFieldCipher cipher)
    {
        _db = db;
        _graph = graph;
        _cipher = cipher;
    }

    public async Task<BusinessProfileDto?> HandleAsync(
        UpdateBusinessProfileCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;
        var errors = new Dictionary<string, string[]>();
        if (req.About?.Length > 139) errors["about"] = ["About must be 139 characters or fewer."];
        if (req.Address?.Length > 256) errors["address"] = ["Address must be 256 characters or fewer."];
        if (req.Description?.Length > 512) errors["description"] = ["Description must be 512 characters or fewer."];
        if (req.Email?.Length > 128) errors["email"] = ["Email must be 128 characters or fewer."];
        else if (!string.IsNullOrEmpty(req.Email) && !req.Email.Contains('@'))
            errors["email"] = ["Email must be a valid email address."];
        if (req.Websites?.Length > 2) errors["websites"] = ["WhatsApp allows at most 2 websites."];
        if (errors.Count > 0) throw new ValidationException(errors);

        var phone = await _db.WabaPhoneNumbers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PhoneNumberId, cancellationToken);
        if (phone is null) return null;

        var (_, token) = await OnboardingGraphSync.RequireTokenAsync(
            _db, _cipher, phone.BusinessAccountId, cancellationToken);

        var graphProfile = new GraphBusinessProfile(
            req.About, req.Address, req.Description, req.Email,
            req.Websites ?? [], req.Vertical, req.ProfilePictureUrl);
        var result = await _graph.UpdateBusinessProfileAsync(
            token, phone.MetaPhoneNumberId, graphProfile, cancellationToken);
        if (!result.Success)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["about"] = [result.Error ?? "Meta rejected the business profile update."],
            });
        }

        var now = DateTimeOffset.UtcNow;
        var profile = await _db.WabaBusinessProfiles
            .FirstOrDefaultAsync(bp => bp.PhoneNumberId == phone.Id, cancellationToken);
        if (profile is null)
        {
            profile = new WabaBusinessProfile
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                PhoneNumberId = phone.Id,
                CreatedAt = now,
                CreatedBy = command.ActorId,
                Version = 0,
            };
            _db.WabaBusinessProfiles.Add(profile);
        }

        profile.About = req.About;
        profile.Address = req.Address;
        profile.Description = req.Description;
        profile.Email = req.Email;
        profile.Websites = req.Websites ?? [];
        profile.Vertical = req.Vertical;
        profile.ProfilePictureUrl = req.ProfilePictureUrl;
        profile.UpdatedAt = now;
        profile.UpdatedBy = command.ActorId;
        profile.Version++;

        await _db.SaveChangesAsync(cancellationToken);

        return new BusinessProfileDto(
            profile.About, profile.Address, profile.Description, profile.Email,
            profile.Websites, profile.Vertical, profile.ProfilePictureUrl);
    }
}
