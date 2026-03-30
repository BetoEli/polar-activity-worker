using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Paw.Polar;

namespace Paw.Test;

[TestFixture]
public class PolarWebhookVerifierTests
{
    private const string TestSecret = "test-webhook-secret-key";

    private static string ComputeExpectedSignature(string payload, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(data)).ToLowerInvariant();
    }

    private static PolarWebhookVerifier BuildVerifier(string? secret = TestSecret)
    {
        var options = Options.Create(new PolarOptions
        {
            WebhookSignatureSecret = secret ?? ""
        });
        return new PolarWebhookVerifier(options, NullLogger<PolarWebhookVerifier>.Instance);
    }

    [Test]
    public void VerifySignature_ValidHmac_ReturnsTrue()
    {
        var verifier = BuildVerifier();
        var payload = """{"event":"EXERCISE","user_id":12345}""";
        var sig = ComputeExpectedSignature(payload, TestSecret);

        verifier.VerifySignature(payload, sig).Should().BeTrue();
    }

    [Test]
    public void VerifySignature_WrongSignature_ReturnsFalse()
    {
        var verifier = BuildVerifier();
        verifier.VerifySignature("payload", "deadbeef").Should().BeFalse();
    }

    [Test]
    public void VerifySignature_SignatureIsCaseInsensitive()
    {
        var verifier = BuildVerifier();
        var payload = "hello";
        var sig = ComputeExpectedSignature(payload, TestSecret).ToUpperInvariant();

        verifier.VerifySignature(payload, sig).Should().BeTrue();
    }

    [Test]
    public void VerifySignature_NoSecretConfigured_ReturnsFalse()
    {
        var verifier = BuildVerifier(secret: "");
        var payload = "hello";
        var sig = ComputeExpectedSignature(payload, TestSecret);

        verifier.VerifySignature(payload, sig).Should().BeFalse();
    }

    [Test]
    public void VerifySignature_TamperedPayload_ReturnsFalse()
    {
        var verifier = BuildVerifier();
        var original = """{"event":"EXERCISE","user_id":12345}""";
        var tampered = """{"event":"EXERCISE","user_id":99999}""";
        var sig = ComputeExpectedSignature(original, TestSecret);

        verifier.VerifySignature(tampered, sig).Should().BeFalse();
    }
}
