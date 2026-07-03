namespace wavio.Utilities.Exceptions;

/// <summary>
/// A business-rule violation that carries a stable machine-readable <see cref="Code"/> plus
/// structured <see cref="Fields"/> for the client to act on programmatically (beyond the
/// human-readable message). Maps to HTTP 422 Unprocessable Entity; the code and fields are
/// surfaced in the response envelope's <c>errorMessage</c> dictionary (each value as a
/// single-element string array, matching the existing error shape) so a client can branch on
/// the code and read the field values without parsing the message text.
/// </summary>
public class StructuredBusinessRuleException : AppExceptionBase
{
    private const string DefaultTitle = "Business Rule Violation";

    /// <summary>Stable identifier for the specific rule, e.g. <c>min_order_value_not_met</c>.</summary>
    public string Code { get; }

    /// <summary>Extra machine-readable fields (already formatted as strings) keyed by name.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; }

    public StructuredBusinessRuleException(
        string code, string message, IReadOnlyDictionary<string, string>? fields = null)
        : base(DefaultTitle, message)
    {
        Code = code;
        Fields = fields ?? new Dictionary<string, string>();
    }
}
