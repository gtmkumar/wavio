using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Common;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetAccessPeople;

public sealed record GetAccessPeopleQuery(string? Search, int Page, int PageSize, string? Sort = null)
    : IQuery<AccessPeoplePageDto>;

public class GetAccessPeopleQueryHandler : IQueryHandler<GetAccessPeopleQuery, AccessPeoplePageDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public GetAccessPeopleQueryHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AccessPeoplePageDto> HandleAsync(GetAccessPeopleQuery q, CancellationToken ct)
    {
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status != "deleted")
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.Status,
                u.UserType,
                u.LastActiveAt,
                u.LastLoginAt,
                First = u.Profile != null ? u.Profile.FirstName : null,
                Last = u.Profile != null ? u.Profile.LastName : null,
                Display = u.Profile != null ? u.Profile.DisplayName : null,
                Membership = u.ScopeMemberships
                    .Where(m => m.RevokedAt == null)
                    .OrderByDescending(m => m.IsPrimary)
                    .Select(m => new { m.ScopeType, m.ScopeId, RoleCode = m.Role.Code, RoleName = m.Role.Name, RoleScope = m.Role.ScopeType })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Tenant isolation: a tenant-scoped admin (or a platform admin who has selected a tenant
        // via X-Tenant-Id) must only see people whose primary membership resolves to that same
        // tenant — otherwise the directory leaks other tenants' staff. Degrades gracefully (shows
        // everything) for a platform admin with no tenant context.
        if (_user.TryGetTenantId() is Guid tenantId)
        {
            users = users.Where(u =>
            {
                var m = u.Membership;
                if (m is null) return _user.IsPlatformAdmin; // unscoped / no-role: platform admins only
                return m.ScopeType switch
                {
                    "platform" => _user.IsPlatformAdmin,
                    "tenant"   => m.ScopeId == tenantId,
                    _ => false,
                };
            }).ToList();
        }

        string ScopeLabel(string? scopeType) => scopeType switch
        {
            null => "—",
            "platform" => "Platform",
            "tenant" => "Tenant",
            _ => "—",
        };

        var people = users.Select(u =>
        {
            var name = !string.IsNullOrWhiteSpace(u.Display) ? u.Display!
                     : $"{u.First} {u.Last}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = u.Email ?? "Unknown";
            var roleScope = u.Membership?.RoleScope ?? "tenant";
            return new PersonDto(
                u.Id, name, u.Email ?? "", AccessHelpers.Initials(name),
                u.Membership?.RoleCode ?? "—",
                u.Membership?.RoleName ?? "No role",
                ScopeLabel(u.Membership?.ScopeType),
                AccessHelpers.Tier(roleScope),
                u.Status,
                u.UserType,
                u.LastActiveAt ?? u.LastLoginAt);
        }).ToList();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            people = people.Where(p =>
                p.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                p.Email.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                p.RoleName.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Sort: explicit column (name/role/active, '-' prefix = descending) or the
        // default tiered order (platform first, then most-recently-active).
        people = (q.Sort switch
        {
            "name" => people.OrderBy(p => p.Name),
            "-name" => people.OrderByDescending(p => p.Name),
            "role" => people.OrderBy(p => p.RoleName),
            "-role" => people.OrderByDescending(p => p.RoleName),
            "active" => people.OrderBy(p => p.LastActiveAt ?? DateTimeOffset.MinValue),
            "-active" => people.OrderByDescending(p => p.LastActiveAt ?? DateTimeOffset.MinValue),
            _ => people.OrderByDescending(p => p.Tier == "platform")
                       .ThenByDescending(p => p.LastActiveAt ?? DateTimeOffset.MinValue),
        }).ToList();

        var counts = new PeopleCountsDto(
            All: people.Count,
            PlatformStaff: people.Count(p => p.Tier == "platform"),
            TenantStaff: people.Count(p => p.Tier == "tenant"));

        // Counts reflect the full (search-filtered) set; the list itself is paged.
        var pagedPeople = PaginatedList<PersonDto>.Create(people, q.Page, q.PageSize);
        return new AccessPeoplePageDto(counts, pagedPeople);
    }
}
