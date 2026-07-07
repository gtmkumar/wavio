using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Commands.CompleteEmbeddedSignup;
using WaAdmin.Application.Onboarding.Dtos;
using WaAdmin.Tests.Fakes;
using wavio.Utilities.Exceptions;
using Moq;
using Xunit;

namespace WaAdmin.Tests.Onboarding;

/// <summary>
/// The Embedded Signup server side (docs/ONBOARDING_WIZARD_PLAN.md): code → token → WABA
/// discovery → encrypted persistence → webhook subscribe. The critical properties: the token is
/// never stored in plaintext, and re-running the signup upserts instead of duplicating.
/// </summary>
public class CompleteEmbeddedSignupCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string Code = "SIMCODE-test";
    private const string Token = "stub-business-token-123";
    private const string WabaId = "10123";
    private const string PhoneId = "20123";

    private static Mock<IWhatsAppOnboardingGraphClient> HappyGraph()
    {
        var graph = new Mock<IWhatsAppOnboardingGraphClient>();
        graph.Setup(g => g.ExchangeCodeAsync(Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTokenResult(true, Token, null));
        graph.Setup(g => g.GetGrantedWabaIdsAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync([WabaId]);
        graph.Setup(g => g.GetBusinessAccountAsync(Token, WabaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphWabaInfo(WabaId, "Demo Business", "INR", "ns_x", "pending"));
        graph.Setup(g => g.GetPhoneNumbersAsync(Token, WabaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GraphPhoneInfo(PhoneId, "+91 90000 00001", "Demo", "PENDING",
                "NOT_VERIFIED", "NONE", "GREEN", "TIER_1K")]);
        graph.Setup(g => g.SubscribeAppAsync(Token, WabaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GraphOpResult.Ok);
        return graph;
    }

    private static CompleteEmbeddedSignupCommand Command() =>
        new(new EmbeddedSignupRequest(Code, null, null), TenantId, null);

    [Fact]
    public async Task HandleAsync_ValidCode_PersistsEncryptedTokenPhoneAndWebhookEvidence()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_ValidCode_PersistsEncryptedTokenPhoneAndWebhookEvidence));
        var handler = new CompleteEmbeddedSignupCommandHandler(db, HappyGraph().Object, new FakeFieldCipher());

        var status = await handler.HandleAsync(Command(), CancellationToken.None);

        Assert.True(status.Connected);
        var account = Assert.Single(db.WabaBusinessAccounts.ToList());
        Assert.Equal(WabaId, account.MetaWabaId);
        Assert.Equal("pending", account.VerificationStatus);
        Assert.NotNull(account.WebhooksSubscribedAt);
        // Envelope encryption at the persistence boundary: ciphertext, never the raw token.
        Assert.NotEqual(Token, account.SystemUserTokenCiphertext);
        Assert.StartsWith(FakeFieldCipher.Prefix, account.SystemUserTokenCiphertext!, StringComparison.Ordinal);

        var phone = Assert.Single(db.WabaPhoneNumbers.ToList());
        Assert.Equal(PhoneId, phone.MetaPhoneNumberId);
        Assert.Equal("PENDING", phone.Status);
        Assert.Equal(TenantId, phone.TenantId);
    }

    [Fact]
    public async Task HandleAsync_RunTwiceForSameWaba_UpsertsInsteadOfDuplicating()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_RunTwiceForSameWaba_UpsertsInsteadOfDuplicating));
        var handler = new CompleteEmbeddedSignupCommandHandler(db, HappyGraph().Object, new FakeFieldCipher());

        await handler.HandleAsync(Command(), CancellationToken.None);
        var firstVersion = Assert.Single(db.WabaBusinessAccounts.ToList()).Version;
        await handler.HandleAsync(Command(), CancellationToken.None);

        var account = Assert.Single(db.WabaBusinessAccounts.ToList());
        Assert.Single(db.WabaPhoneNumbers.ToList());
        Assert.Equal(firstVersion + 1, account.Version);
    }

    [Fact]
    public async Task HandleAsync_ExchangeRejected_ThrowsValidationAndPersistsNothing()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_ExchangeRejected_ThrowsValidationAndPersistsNothing));
        var graph = new Mock<IWhatsAppOnboardingGraphClient>();
        graph.Setup(g => g.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTokenResult(false, null, "Bad code."));
        var handler = new CompleteEmbeddedSignupCommandHandler(db, graph.Object, new FakeFieldCipher());

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => handler.HandleAsync(Command(), CancellationToken.None));

        Assert.Contains("code", ex.ErrorsDictionary!.Keys);
        Assert.Empty(db.WabaBusinessAccounts.ToList());
    }
}
