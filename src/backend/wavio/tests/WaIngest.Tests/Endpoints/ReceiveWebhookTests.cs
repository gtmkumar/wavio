using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WaIngest.Application.Common.Interfaces;
using WaIngest.Application.Common.Options;
using WaIngest.Application.Ingestion;
using WaIngest.Application.Ingestion.Commands.PersistRawWebhook;
using WaIngest.Application.Ingestion.Dtos;
using WaIngest.WebApi.Endpoints;
using Wavio.Utilities.CQRS.Abstractions;
using Xunit;

namespace WaIngest.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="Webhooks.ReceiveWebhook"/> directly (internal, exposed via
/// InternalsVisibleTo) against a fabricated <see cref="DefaultHttpContext"/> — no real HTTP
/// server needed since the delegate takes everything as explicit parameters.
/// </summary>
public class ReceiveWebhookTests
{
    private const string AppSecret = "test-app-secret";

    private static string SignatureFor(string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static DefaultHttpContext CreateContext(byte[] bodyBytes, string? signatureHeader, long? contentLength)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = contentLength;
        if (signatureHeader is not null)
            context.Request.Headers["X-Hub-Signature-256"] = signatureHeader;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5");
        return context;
    }

    private static (Mock<IDispatcher> Dispatcher, Func<PersistRawWebhookCommand?> LastCommand) MockDispatcher(RawWebhookRef toReturn)
    {
        PersistRawWebhookCommand? captured = null;
        var dispatcher = new Mock<IDispatcher>();
        dispatcher
            .Setup(d => d.SendAsync(It.IsAny<ICommand<RawWebhookRef>>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand<RawWebhookRef>, CancellationToken>((cmd, _) => captured = cmd as PersistRawWebhookCommand)
            .ReturnsAsync(toReturn);
        return (dispatcher, () => captured);
    }

    private static IOptions<MetaWebhookOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new MetaWebhookOptions { AppSecret = AppSecret, VerifyToken = "unused" });

    [Fact]
    public async Task ReceiveWebhook_InvalidSignature_PersistsOnlyAFixedStubNeverTheRealBody()
    {
        // Regression test (security review, B1): an unsigned/forged request must never get its
        // own body written to Postgres — only a small fixed-shape stub, so an unauthenticated
        // caller can't use this endpoint to fill the shared database with arbitrary bytes.
        const string attackerBody = """{"object":"whatsapp_business_account","entry":[{"id":"attacker-controlled-marker-xyz"}]}""";
        var bodyBytes = Encoding.UTF8.GetBytes(attackerBody);
        var context = CreateContext(bodyBytes, "sha256=" + new string('0', 64), bodyBytes.Length);

        var (dispatcher, lastCommand) = MockDispatcher(new RawWebhookRef(Guid.NewGuid(), DateTimeOffset.UtcNow));
        var buffer = new Mock<IWebhookIngestBuffer>();

        var result = await Webhooks.ReceiveWebhook(
            context, dispatcher.Object, buffer.Object, Options(), NullLogger<Webhooks>.Instance, CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, statusResult.StatusCode);

        var command = lastCommand();
        Assert.NotNull(command);
        Assert.False(command!.SignatureValid);
        Assert.DoesNotContain("attacker-controlled-marker-xyz", command.Payload, StringComparison.Ordinal);
        Assert.Contains("signature_invalid", command.Payload, StringComparison.Ordinal);

        buffer.Verify(b => b.EnqueueAsync(It.IsAny<RawWebhookRef>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReceiveWebhook_MissingSignatureHeader_PersistsOnlyStub()
    {
        const string attackerBody = """{"object":"whatsapp_business_account","entry":[{"id":"no-header-at-all"}]}""";
        var bodyBytes = Encoding.UTF8.GetBytes(attackerBody);
        var context = CreateContext(bodyBytes, signatureHeader: null, bodyBytes.Length);

        var (dispatcher, lastCommand) = MockDispatcher(new RawWebhookRef(Guid.NewGuid(), DateTimeOffset.UtcNow));
        var buffer = new Mock<IWebhookIngestBuffer>();

        var result = await Webhooks.ReceiveWebhook(
            context, dispatcher.Object, buffer.Object, Options(), NullLogger<Webhooks>.Instance, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.DoesNotContain("no-header-at-all", lastCommand()!.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReceiveWebhook_ValidSignature_PersistsRealBodyVerbatimAndEnqueues()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var context = CreateContext(bodyBytes, SignatureFor(body), bodyBytes.Length);

        var reference = new RawWebhookRef(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var (dispatcher, lastCommand) = MockDispatcher(reference);
        var buffer = new Mock<IWebhookIngestBuffer>();

        var result = await Webhooks.ReceiveWebhook(
            context, dispatcher.Object, buffer.Object, Options(), NullLogger<Webhooks>.Instance, CancellationToken.None);

        Assert.IsType<Ok>(result);

        var command = lastCommand();
        Assert.NotNull(command);
        Assert.True(command!.SignatureValid);
        Assert.Equal(body, command.Payload);

        buffer.Verify(b => b.EnqueueAsync(reference, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveWebhook_OversizedBodyWithNoContentLengthHeader_Returns413WithoutPersisting()
    {
        // Regression test (security review, S1): a chunked-encoded request carries no
        // Content-Length header at all, so the fast-path length check alone would never catch
        // it — the bound must be enforced on the actual bytes read, not the declared length.
        var oversized = new byte[Webhooks.MaxBodyBytes + 1000];
        var context = CreateContext(oversized, signatureHeader: null, contentLength: null);

        var dispatcher = new Mock<IDispatcher>();
        var buffer = new Mock<IWebhookIngestBuffer>();

        var result = await Webhooks.ReceiveWebhook(
            context, dispatcher.Object, buffer.Object, Options(), NullLogger<Webhooks>.Instance, CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        dispatcher.Verify(d => d.SendAsync(It.IsAny<ICommand<RawWebhookRef>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryReadBoundedBodyAsync_BodyExceedsLimit_ReturnsNull()
    {
        var oversized = new byte[Webhooks.MaxBodyBytes + 1000];
        await using var stream = new MemoryStream(oversized);

        var result = await Webhooks.TryReadBoundedBodyAsync(stream, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryReadBoundedBodyAsync_BodyWithinLimit_ReturnsExactBytes()
    {
        var body = Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account","entry":[]}""");
        await using var stream = new MemoryStream(body);

        var result = await Webhooks.TryReadBoundedBodyAsync(stream, CancellationToken.None);

        Assert.Equal(body, result);
    }
}
