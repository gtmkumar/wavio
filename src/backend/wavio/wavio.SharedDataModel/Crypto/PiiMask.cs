namespace wavio.SharedDataModel.Crypto;

/// <summary>
/// Masking helpers for financial PII returned in API responses.
///
/// Masking rules (chosen to be format-preserving enough to be useful in UI without leaking):
///   PAN            "ABCDE1234F" → "XXXXX1234F"  (last 5 chars visible — matches IT-dept convention)
///   Bank account   "123456789012" → "••••9012"   (last 4 digits; any length ≥ 4)
///   UPI ID         "name@bank" → "••••@bank"     (handle replaced with bullets; @domain retained)
///   IFSC           returned as-is (publicly listed branch code, not a secret)
/// </summary>
public static class PiiMask
{
    /// <summary>
    /// Masks a PAN: last 5 characters visible, first 5 replaced with 'X'.
    /// Returns null for null/empty input.
    /// </summary>
    public static string? MaskPan(string? pan)
    {
        if (string.IsNullOrEmpty(pan)) return pan;
        // Standard PAN is 10 chars; tolerate shorter values gracefully.
        if (pan.Length <= 5) return new string('X', pan.Length);
        return new string('X', pan.Length - 5) + pan[^5..];
    }

    /// <summary>
    /// Masks a bank account number: last 4 digits visible, rest replaced with '•'.
    /// Returns null for null/empty input.
    /// </summary>
    public static string? MaskBankAccount(string? accountNumber)
    {
        if (string.IsNullOrEmpty(accountNumber)) return accountNumber;
        if (accountNumber.Length < 4) return new string('•', accountNumber.Length);
        return new string('•', accountNumber.Length - 4) + accountNumber[^4..];
    }

    /// <summary>
    /// Masks a UPI ID: replaces the local-part (before '@') with '••••', retaining '@domain'.
    /// If no '@' is found, replaces the whole value with '••••'.
    /// Returns null for null/empty input.
    /// </summary>
    public static string? MaskUpi(string? upiId)
    {
        if (string.IsNullOrEmpty(upiId)) return upiId;
        var at = upiId.IndexOf('@');
        return at > 0 ? "••••" + upiId[at..] : "••••";
    }
}
