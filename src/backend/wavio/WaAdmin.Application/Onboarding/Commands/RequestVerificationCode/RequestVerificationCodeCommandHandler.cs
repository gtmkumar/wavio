using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Logic;
using wavio.SharedDataModel.Crypto;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Onboarding.Commands.RequestVerificationCode;

public sealed class RequestVerificationCodeCommandHandler
    : ICommandHandler<RequestVerificationCodeCommand, bool>
{
    private static readonly HashSet<string> ValidMethods = ["SMS", "VOICE"];

    private readonly IWaAdminDbContext _db;
    private readonly IWhatsAppOnboardingGraphClient _graph;
    private readonly IFieldCipher _cipher;

    public RequestVerificationCodeCommandHandler(
        IWaAdminDbContext db, IWhatsAppOnboardingGraphClient graph, IFieldCipher cipher)
    {
        _db = db;
        _graph = graph;
        _cipher = cipher;
    }

    public async Task<bool> HandleAsync(
        RequestVerificationCodeCommand command, CancellationToken cancellationToken)
    {
        var method = string.IsNullOrWhiteSpace(command.CodeMethod) ? "SMS" : command.CodeMethod.ToUpperInvariant();
        if (!ValidMethods.Contains(method))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["codeMethod"] = ["codeMethod must be SMS or VOICE."],
            });
        }

        var phone = await _db.WabaPhoneNumbers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PhoneNumberId, cancellationToken);
        if (phone is null) return false;

        var (_, token) = await OnboardingGraphSync.RequireTokenAsync(
            _db, _cipher, phone.BusinessAccountId, cancellationToken);

        var result = await _graph.RequestVerificationCodeAsync(
            token, phone.MetaPhoneNumberId, method,
            string.IsNullOrWhiteSpace(command.Language) ? "en_US" : command.Language,
            cancellationToken);
        if (!result.Success)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["codeMethod"] = [result.Error ?? "Meta could not send a verification code."],
            });
        }

        return true;
    }
}
