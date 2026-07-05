using WaAdmin.Application.Consent.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.RetentionPolicies.Commands.UpsertRetentionPolicy;

/// <summary>PUT /v1/consent/retention-policies — upserts THIS TENANT's override row for one data
/// class. Never touches the platform-default (NULL-tenant) row — see
/// UpsertRetentionPolicyRequest's doc comment.</summary>
public sealed record UpsertRetentionPolicyCommand(
    UpsertRetentionPolicyRequest Request, Guid TenantId, Guid? ActorId)
    : ICommand<RetentionPolicyDto>;
