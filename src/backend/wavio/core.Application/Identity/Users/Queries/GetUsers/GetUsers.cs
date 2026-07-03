using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Common;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Users.Queries.GetUsers;

public sealed record GetUsersQuery(int Page = 1, int PageSize = 20, string? Status = null, string? UserType = null, string? Search = null)
    : IQuery<PaginatedList<UserDto>>;

public class GetUsersQueryHandler : IQueryHandler<GetUsersQuery, PaginatedList<UserDto>>
{
    private readonly ICoreDbContext _db;
    public GetUsersQueryHandler(ICoreDbContext db) => _db = db;

    public Task<PaginatedList<UserDto>> HandleAsync(GetUsersQuery r, CancellationToken ct)
    {
        var q = _db.Users.AsNoTracking()
            .Include(u => u.Profile)
            .AsQueryable();
        if (!string.IsNullOrEmpty(r.Status))   q = q.Where(u => u.Status   == r.Status);
        if (!string.IsNullOrEmpty(r.UserType)) q = q.Where(u => u.UserType == r.UserType);
        if (!string.IsNullOrEmpty(r.Search))
            q = q.Where(u => (u.Email != null && u.Email.Contains(r.Search))
                           || (u.PhoneE164 != null && u.PhoneE164.Contains(r.Search)));
        return PaginatedList<UserDto>.CreateAsync(
            q.OrderByDescending(u => u.CreatedAt).Select(u => new UserDto(
                u.Id, u.Email, u.PhoneE164, u.UserType, u.Status, u.MfaEnabled, u.LastLoginAt, u.CreatedAt,
                u.Profile != null ? u.Profile.FirstName : null,
                u.Profile != null ? u.Profile.LastName  : null,
                u.Profile != null ? u.Profile.DisplayName : null)),
            r.Page, r.PageSize, ct);
    }
}
