using core.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Auth.Queries.GetInvitePreview;

public sealed class GetInvitePreviewHandler : IQueryHandler<GetInvitePreviewQuery, InvitePreviewDto>
{
    private readonly ICoreDbContext _db;
    public GetInvitePreviewHandler(ICoreDbContext db) => _db = db;

    public async Task<InvitePreviewDto> HandleAsync(GetInvitePreviewQuery q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q.Token)) return new InvitePreviewDto(false, null, null);

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.InvitationToken == q.Token && u.Status == UserStatus.Invited)
            .Select(u => new { u.Id, u.Email })
            .FirstOrDefaultAsync(ct);
        if (user is null) return new InvitePreviewDto(false, null, null);

        var name = await _db.UserProfiles.AsNoTracking().Where(p => p.UserId == user.Id)
            .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim())
            .FirstOrDefaultAsync(ct);

        return new InvitePreviewDto(true, user.Email, string.IsNullOrWhiteSpace(name) ? null : name);
    }
}
