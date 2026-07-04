using System.Security.Cryptography;
using System.Text;
using WaIngest.Application.Security;
using Xunit;

namespace WaIngest.Tests.Security;

public class MetaWebhookSignatureVerifierTests
{
    private const string AppSecret = "test-app-secret";

    private static string ComputeSignatureHeader(string body, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";
        var header = ComputeSignatureHeader(body, AppSecret);

        var result = MetaWebhookSignatureVerifier.Verify(Encoding.UTF8.GetBytes(body), header, AppSecret);

        Assert.True(result);
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";
        var header = ComputeSignatureHeader(body, "a-different-secret");

        var result = MetaWebhookSignatureVerifier.Verify(Encoding.UTF8.GetBytes(body), header, AppSecret);

        Assert.False(result);
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsFalse()
    {
        const string originalBody = """{"object":"whatsapp_business_account","entry":[]}""";
        var header = ComputeSignatureHeader(originalBody, AppSecret);

        const string tamperedBody = """{"object":"whatsapp_business_account","entry":[{"id":"1"}]}""";

        var result = MetaWebhookSignatureVerifier.Verify(Encoding.UTF8.GetBytes(tamperedBody), header, AppSecret);

        Assert.False(result);
    }

    [Fact]
    public void Verify_MissingHeader_ReturnsFalse()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";

        var result = MetaWebhookSignatureVerifier.Verify(Encoding.UTF8.GetBytes(body), null, AppSecret);

        Assert.False(result);
    }

    [Fact]
    public void Verify_EmptyHeader_ReturnsFalse()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";

        var result = MetaWebhookSignatureVerifier.Verify(Encoding.UTF8.GetBytes(body), string.Empty, AppSecret);

        Assert.False(result);
    }

    [Fact]
    public void Verify_MissingPrefix_ReturnsFalse()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), Encoding.UTF8.GetBytes(body));
        var headerWithoutPrefix = Convert.ToHexString(hash).ToLowerInvariant(); // no "sha256=" prefix

        var result = MetaWebhookSignatureVerifier.Verify(Encoding.UTF8.GetBytes(body), headerWithoutPrefix, AppSecret);

        Assert.False(result);
    }

    [Fact]
    public void Verify_MalformedHex_ReturnsFalse()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";

        var result = MetaWebhookSignatureVerifier.Verify(Encoding.UTF8.GetBytes(body), "sha256=not-hex-zzzz", AppSecret);

        Assert.False(result);
    }

    [Fact]
    public void Verify_EmptyAppSecret_ReturnsFalse()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";
        var header = ComputeSignatureHeader(body, AppSecret);

        var result = MetaWebhookSignatureVerifier.Verify(Encoding.UTF8.GetBytes(body), header, string.Empty);

        Assert.False(result);
    }
}
