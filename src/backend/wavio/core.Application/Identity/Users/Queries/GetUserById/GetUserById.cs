using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Crypto;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Users.Queries.GetUserById;

public sealed record GetUserByIdQuery(Guid Id) : IQuery<UserDto?>;

public class GetUserByIdQueryHandler : IQueryHandler<GetUserByIdQuery, UserDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;
    public GetUserByIdQueryHandler(ICoreDbContext db, ICurrentUser actor) { _db = db; _actor = actor; }

    public async Task<UserDto?> HandleAsync(GetUserByIdQuery r, CancellationToken ct)
    {
        var dto = await _db.Users.AsNoTracking().Include(u => u.Profile)
            .Where(u => u.Id == r.Id)
            .Select(u => new UserDto(
                u.Id, u.Email, u.PhoneE164, u.UserType, u.Status, u.MfaEnabled, u.LastLoginAt, u.CreatedAt,
                u.Profile != null ? u.Profile.FirstName       : null,
                u.Profile != null ? u.Profile.LastName        : null,
                u.Profile != null ? u.Profile.DisplayName     : null,
                u.Profile != null ? u.Profile.Designation     : null,
                u.Profile != null ? u.Profile.EmploymentType  : null,
                u.Profile != null ? u.Profile.PanNumber       : null,
                u.Profile != null ? u.Profile.AadhaarNumberMasked : null,
                u.Profile != null ? u.Profile.KycStatus       : null,
                u.Profile != null ? u.Profile.KycVerifiedAt   : null,
                u.Profile != null ? u.Profile.BankAccountName : null,
                u.Profile != null ? u.Profile.BankAccountNumber : null,
                u.Profile != null ? u.Profile.BankIfsc        : null,
                u.Profile != null ? u.Profile.UpiId           : null))
            .FirstOrDefaultAsync(ct);

        if (dto is null) return null;

        // Apply financial PII masking unless the caller holds users.read_financial.
        return UserDtoFinancialMask.Apply(dto, _actor);
    }
}
