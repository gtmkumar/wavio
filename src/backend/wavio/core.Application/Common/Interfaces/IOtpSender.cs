namespace core.Application.Common.Interfaces;

/// <summary>Seam for OTP dispatch. Dev implementation (DevLogOtpSender) logs the code.
/// Swap the DI registration for a real SMS/WhatsApp/email provider in production.</summary>
public interface IOtpSender
{
    Task SendAsync(string identifier, string identifierType, string plainCode, string purpose, CancellationToken ct = default, Guid? tenantId = null);
}
