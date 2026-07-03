namespace core.Application.Identity.AccessControl.Dtos;

// ── People tab ──────────────────────────────────────────────────────────────
public sealed record PersonDto(
    Guid Id, string Name, string Email, string Initials,
    string RoleCode, string RoleName, string ScopeLabel,
    string Tier, string Status,
    // The account's coarse user_type (e.g. ops_staff / warehouse_staff). The role *code* alone
    // can't distinguish the vertical-neutral ops_staff (it shares role codes), so the directory
    // needs the type to badge and humanise it. See SharedDataModel/Enums/UserType.
    string UserType,
    DateTimeOffset? LastActiveAt);

public sealed record PeopleCountsDto(int All, int PlatformStaff, int TenantStaff);

public sealed record AccessPeopleDto(PeopleCountsDto Counts, IReadOnlyList<PersonDto> People);

/// <summary>Paged people response: aggregate counts (full set) + the current page of people.</summary>
public sealed record AccessPeoplePageDto(
    PeopleCountsDto Counts,
    wavio.Utilities.Common.PaginatedList<PersonDto> People);

// ── Roles & Permissions tab ─────────────────────────────────────────────────
public sealed record MatrixModuleDto(string Key, string Label);

public sealed record RoleSummaryDto(
    Guid Id, string Code, string Name, string? Description,
    string ScopeType, bool IsSystem, int MemberCount,
    IReadOnlyList<string> OnCells); // "module:action" cells that are enabled

public sealed record RoleGroupDto(string Tier, string TierLabel, IReadOnlyList<RoleSummaryDto> Roles);

public sealed record AccessRolesDto(
    IReadOnlyList<MatrixModuleDto> Modules,
    IReadOnlyList<string> Actions,
    IReadOnlyList<RoleGroupDto> Groups,
    // cellKey ("module:action") → the permission codes that cell grants (for the UI fan-out tooltip).
    IReadOnlyDictionary<string, IReadOnlyList<string>> Cells);

// ── Write payloads ──────────────────────────────────────────────────────────
/// <summary>Invite = create user + grant a primary role within a scope.</summary>
public sealed record InviteUserRequest(
    string Email, string? Phone, string? FirstName, string? LastName,
    string UserType, Guid RoleId, string ScopeType, Guid? ScopeId, string? Password);

/// <summary>One cell change in a batch save.</summary>
public sealed record RoleCellChange(string CellKey, bool Enabled);
/// <summary>Apply many cell changes to one role atomically (single transaction).</summary>
public sealed record SetRoleCellsRequest(IReadOnlyList<RoleCellChange> Changes);

/// <summary>
/// Change a person's account status. <c>Action</c> is one of
/// <c>activate</c> (invited → active, sets the temp password),
/// <c>suspend</c> (active → suspended) or <c>reactivate</c> (suspended → active).
/// </summary>
public sealed record SetPersonStatusRequest(string Action, string? Password);

/// <summary>Result of a status change — the new status plus whether a first-login reset is required.</summary>
public sealed record SetPersonStatusResult(string Status, bool MustChangePassword);
