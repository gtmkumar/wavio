using WaIntel.Application.Windows.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaIntel.Application.Windows.Commands.UpsertWindowOnMessageReceived;

/// <summary>
/// Upserts <c>sessions.conversation_windows</c> for one inbound consumer message. Tenant
/// resolution has ALREADY happened by the time this command is dispatched — see
/// <c>ITenantResolver</c> and the consumer in WaIntel.Infrastructure/Messaging. This handler
/// never sees, and never needs to know about, the raw Meta identifiers or the Wave 1
/// tenant-resolution gap.
/// </summary>
public sealed record UpsertWindowOnMessageReceivedCommand(
    Guid TenantId,
    Guid PhoneNumberId,
    string UserWaId,
    DateTimeOffset SentAt,
    bool OpensCustomerServiceWindow,
    /// <summary>Raw Meta <c>referral</c> object (jsonb), present only when this message carries a
    /// CTWA/fb_cta referral — i.e. a CTWA window entry. Null for an ordinary organic message.</summary>
    string? CtwaReferralJson) : ICommand<WindowStateDto>;
