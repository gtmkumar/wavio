namespace Wavio.Utilities.CQRS.Context;

/// <summary>
/// Carries the correlation id that ties together every log/behavior entry for a single request.
/// Registered per-scope so all behaviors in one dispatch share the same id.
/// </summary>
public sealed class CorrelationContext
{
    public CorrelationContext()
        : this(Guid.NewGuid().ToString("N"))
    {
    }

    public CorrelationContext(string correlationId)
    {
        CorrelationId = correlationId;
    }

    public string CorrelationId { get; }
}
