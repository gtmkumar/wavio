using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Commands.RegisterPhoneNumber;
using WaAdmin.Tests.Fakes;
using wavio.SharedDataModel.Entities.Waba;
using wavio.Utilities.Exceptions;
using Moq;
using Xunit;

namespace WaAdmin.Tests.Onboarding;

public class RegisterPhoneNumberCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string Token = "stub-business-token-123";

    private static async Task<(InMemoryWaAdminDbContext Db, WabaPhoneNumber Phone)> SeedConnectedAsync(string dbName)
    {
        var db = InMemoryWaAdminDbContext.Create(dbName);
        var now = DateTimeOffset.UtcNow;
        var account = new WabaBusinessAccount
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            MetaWabaId = "10123",
            Name = "Demo",
            Status = "active",
            SystemUserTokenCiphertext = new FakeFieldCipher().Encrypt(Token),
            TokenKeyRef = "master:v1",
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        var phone = new WabaPhoneNumber
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BusinessAccountId = account.Id,
            MetaPhoneNumberId = "20123",
            DisplayPhoneNumber = "+91 90000 00001",
            Status = "PENDING",
            CodeVerificationStatus = "VERIFIED",
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        db.WabaBusinessAccounts.Add(account);
        db.WabaPhoneNumbers.Add(phone);
        await db.SaveChangesAsync(CancellationToken.None);
        return (db, phone);
    }

    [Fact]
    public async Task HandleAsync_GraphAccepts_SetsRegisteredAtAndLogsStatusTransition()
    {
        var (db, phone) = await SeedConnectedAsync(nameof(HandleAsync_GraphAccepts_SetsRegisteredAtAndLogsStatusTransition));
        await using var _ = db;
        var graph = new Mock<IWhatsAppOnboardingGraphClient>();
        graph.Setup(g => g.RegisterPhoneAsync(Token, phone.MetaPhoneNumberId, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GraphOpResult.Ok);
        graph.Setup(g => g.GetPhoneNumberAsync(Token, phone.MetaPhoneNumberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphPhoneInfo(phone.MetaPhoneNumberId, phone.DisplayPhoneNumber, "Demo",
                "CONNECTED", "VERIFIED", "PENDING_REVIEW", "GREEN", "TIER_1K"));
        var handler = new RegisterPhoneNumberCommandHandler(db, graph.Object, new FakeFieldCipher());

        var result = await handler.HandleAsync(
            new RegisterPhoneNumberCommand(phone.Id, "123456", TenantId, null), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("CONNECTED", result!.Status);
        Assert.NotNull(result.RegisteredAt);
        Assert.Equal("PENDING_REVIEW", result.NameStatus);

        // V002's contract: the PENDING → CONNECTED transition is logged in phone_number_events.
        var evt = Assert.Single(db.WabaPhoneNumberEvents.ToList());
        Assert.Equal("PENDING", evt.OldStatus);
        Assert.Equal("CONNECTED", evt.NewStatus);
        Assert.Equal(phone.Id, evt.PhoneNumberId);
    }

    [Fact]
    public async Task HandleAsync_PinNotSixDigits_ThrowsWithoutCallingGraph()
    {
        var (db, phone) = await SeedConnectedAsync(nameof(HandleAsync_PinNotSixDigits_ThrowsWithoutCallingGraph));
        await using var _ = db;
        var graph = new Mock<IWhatsAppOnboardingGraphClient>(MockBehavior.Strict);
        var handler = new RegisterPhoneNumberCommandHandler(db, graph.Object, new FakeFieldCipher());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            new RegisterPhoneNumberCommand(phone.Id, "12ab", TenantId, null), CancellationToken.None));

        Assert.Contains("pin", ex.ErrorsDictionary!.Keys);
        Assert.Empty(db.WabaPhoneNumberEvents.ToList());
    }

    [Fact]
    public async Task HandleAsync_UnknownPhoneId_ReturnsNull()
    {
        var (db, _) = await SeedConnectedAsync(nameof(HandleAsync_UnknownPhoneId_ReturnsNull));
        await using var __ = db;
        var handler = new RegisterPhoneNumberCommandHandler(
            db, new Mock<IWhatsAppOnboardingGraphClient>().Object, new FakeFieldCipher());

        var result = await handler.HandleAsync(
            new RegisterPhoneNumberCommand(Guid.NewGuid(), "123456", TenantId, null), CancellationToken.None);

        Assert.Null(result);
    }
}
