using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Paw.Polar;

public interface IPolarWebhookVerifier
{
    bool VerifySignature(string payload, string signature);
}

public class PolarWebhookVerifier : IPolarWebhookVerifier
{
    private readonly PolarOptions _options;
    private readonly ILogger<PolarWebhookVerifier> _logger;

    public PolarWebhookVerifier(IOptions<PolarOptions> options, ILogger<PolarWebhookVerifier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool VerifySignature(string payload, string signature)
    {
        if (string.IsNullOrEmpty(_options.WebhookSignatureSecret))
        {
            _logger.LogWarning("Webhook signature secret is not configured. Skipping verification.");
            return false;
        }

        try
        {
            var keyBytes = Encoding.UTF8.GetBytes(_options.WebhookSignatureSecret);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(payloadBytes);
            var computedSignature = Convert.ToHexString(hash).ToLowerInvariant();

            var isValid = signature.Equals(computedSignature, StringComparison.OrdinalIgnoreCase);

            if (!isValid)
            {
                _logger.LogWarning("Webhook signature verification failed. Expected: {Expected}, Got: {Actual}", 
                    computedSignature, signature);
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying webhook signature");
            return false;
        }
    }
}

