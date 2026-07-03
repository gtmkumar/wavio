namespace Wavio.Utilities.CQRS.Context;

/// <summary>
/// Ambient metadata for a single dispatched request — correlation, acting user, and start time.
/// Behaviors read this to enrich logs, audit records, and performance traces.
/// </summary>
public sealed class RequestContext
{
    public RequestContext(
        CorrelationContext correlation,
        UserContext user,
        DateTimeOffset startedAt)
    {
        Correlation = correlation;
        User = user;
        StartedAt = startedAt;
    }

    public CorrelationContext Correlation { get; }

    public UserContext User { get; }

    public DateTimeOffset StartedAt { get; }
}
