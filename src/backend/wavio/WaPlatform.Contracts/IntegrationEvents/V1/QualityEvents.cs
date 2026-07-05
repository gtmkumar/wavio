namespace WaPlatform.Contracts.IntegrationEvents.V1;

/// <summary>
/// <c>wa.guardian.incident_opened.v1</c> — the Guardian (issue #20, spec §4.6) opened an incident
/// on a phone number: a quality degradation, a tier downgrade, or a block-rate spike. Published so
/// downstream consumers (tenant alerting — not yet built, out of scope for this issue) can notify
/// the tenant; WaGateway does NOT consume this event — it reads <c>quality.guardian_incidents</c>
/// directly per send (see <c>OutboxDispatcherService</c>'s doc comment for why).
/// </summary>
public sealed record GuardianIncidentOpenedV1 : IntegrationEvent
{
    public const string Name = "wa.guardian.incident_opened.v1";
    public override string EventName => Name;

    public required Guid PhoneNumberId { get; init; }
    public required Guid IncidentId { get; init; }

    /// <summary>quality_yellow | quality_red | tier_downgrade | block_rate_spike | template_paused.</summary>
    public required string IncidentType { get; init; }

    /// <summary>info | warning | critical.</summary>
    public required string Severity { get; init; }

    /// <summary>none | marketing_50pct | marketing_frozen.</summary>
    public required string ThrottleAction { get; init; }
}

/// <summary>
/// <c>wa.guardian.incident_resolved.v1</c> — a previously open Guardian incident was resolved
/// (e.g. quality recovered to GREEN). See <see cref="GuardianIncidentOpenedV1"/> for consumer
/// notes.
/// </summary>
public sealed record GuardianIncidentResolvedV1 : IntegrationEvent
{
    public const string Name = "wa.guardian.incident_resolved.v1";
    public override string EventName => Name;

    public required Guid PhoneNumberId { get; init; }
    public required Guid IncidentId { get; init; }
    public required string IncidentType { get; init; }
}
