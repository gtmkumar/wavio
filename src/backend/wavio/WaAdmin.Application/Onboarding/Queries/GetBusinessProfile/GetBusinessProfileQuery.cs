using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Dtos;
using wavio.SharedDataModel.Crypto;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Onboarding.Queries.GetBusinessProfile;

/// <summary>GET /v1/onboarding/phone-numbers/{id}/profile — the current public profile for the
/// wizard's profile step. Reads Meta live when a token exists (the profile may have been edited
/// outside Wavio), falling back to the local mirror row; returns null only when the phone id is
/// not in the caller's tenant (endpoint answers 404).</summary>
public sealed record GetBusinessProfileQuery(Guid PhoneNumberId) : IQuery<BusinessProfileDto?>;

public sealed class GetBusinessProfileQueryHandler
    : IQueryHandler<GetBusinessProfileQuery, BusinessProfileDto?>
{
    private readonly IWaAdminDbContext _db;
    private readonly IWhatsAppOnboardingGraphClient _graph;
    private readonly IFieldCipher _cipher;

    public GetBusinessProfileQueryHandler(
        IWaAdminDbContext db, IWhatsAppOnboardingGraphClient graph, IFieldCipher cipher)
    {
        _db = db;
        _graph = graph;
        _cipher = cipher;
    }

    public async Task<BusinessProfileDto?> HandleAsync(
        GetBusinessProfileQuery query, CancellationToken cancellationToken)
    {
        var phone = await _db.WabaPhoneNumbers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == query.PhoneNumberId, cancellationToken);
        if (phone is null) return null;

        var account = await _db.WabaBusinessAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == phone.BusinessAccountId, cancellationToken);
        var token = _cipher.Decrypt(account?.SystemUserTokenCiphertext);

        if (!string.IsNullOrEmpty(token))
        {
            var live = await _graph.GetBusinessProfileAsync(token, phone.MetaPhoneNumberId, cancellationToken);
            if (live is not null)
            {
                return new BusinessProfileDto(
                    live.About, live.Address, live.Description, live.Email,
                    live.Websites, live.Vertical, live.ProfilePictureUrl);
            }
        }

        var mirror = await _db.WabaBusinessProfiles.AsNoTracking()
            .FirstOrDefaultAsync(bp => bp.PhoneNumberId == phone.Id, cancellationToken);
        return mirror is null
            ? new BusinessProfileDto(null, null, null, null, [], null, null)
            : new BusinessProfileDto(
                mirror.About, mirror.Address, mirror.Description, mirror.Email,
                mirror.Websites, mirror.Vertical, mirror.ProfilePictureUrl);
    }
}
