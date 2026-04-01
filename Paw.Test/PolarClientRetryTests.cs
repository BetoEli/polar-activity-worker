using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// Verifies the Polly retry policy wired in Program.cs behaves correctly.
/// Uses a fake HttpMessageHandler to control exactly which status codes are
/// returned, so no real network calls are made. Delays are set to zero to
/// keep the suite fast.
/// </summary>
[TestFixture]
[Category("Unit")]
public class PolarClientRetryTests
{
    private const string FakeExerciseJson =
        """{"id":"ex-test","start_time":"2026-01-01T09:00:00","duration":"PT30M","sport":"RUNNING"}""";

    // Mirrors the retry policy in Program.cs / Worker/Program.cs with zero delay.
    private static IPolarClient BuildClient(FakeHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<PolarOptions>(opts =>
        {
            opts.BaseUrl      = "https://www.polaraccesslink.com";
            opts.ClientId     = "test-id";
            opts.ClientSecret = "test-secret";
            opts.RedirectUri  = "http://localhost/callback";
        });

        services.AddHttpClient<IPolarClient, PolarClient>()
            .AddPolicyHandler((svc, _) =>
            {
                var logger = svc.GetRequiredService<ILogger<PolarClient>>();
                return Policy<HttpResponseMessage>
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout)
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: _ => TimeSpan.Zero,
                        onRetry: (_, _, attempt, _) =>
                            logger.LogWarning("Test retry {Attempt}/3", attempt));
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider()
                       .GetRequiredService<IPolarClient>();
    }

    [Test]
    public async Task RetryPolicy_503TwiceThen200_SucceedsOnThirdAttempt()
    {
        var handler = new FakeHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            OkExerciseResponse());

        var client = BuildClient(handler);

        var result = await client.GetExerciseByIdAsync("tok", "ex-test");

        handler.CallCount.Should().Be(3, "1 initial + 2 retries before success");
        result.Should().NotBeNull();
        result!.Value.Exercise!.Id.Should().Be("ex-test");
    }

    [Test]
    public async Task RetryPolicy_429ThenSuccess_RetriesOnce()
    {
        var handler = new FakeHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            OkExerciseResponse());

        var client = BuildClient(handler);

        var result = await client.GetExerciseByIdAsync("tok", "ex-test");

        handler.CallCount.Should().Be(2, "1 initial attempt + 1 retry after 429");
        result.Should().NotBeNull();
    }

    [Test]
    public async Task RetryPolicy_Persistent503_ThrowsPolarApiExceptionAfterThreeRetries()
    {
        var handler = new FakeHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var client = BuildClient(handler);

        var act = async () => await client.GetExerciseByIdAsync("tok", "ex-test");

        await act.Should().ThrowAsync<PolarApiException>(
            "PolarClient must surface the final 503 as a PolarApiException");
        handler.CallCount.Should().Be(4, "1 initial attempt + 3 retries = 4 total calls");
    }

    private static HttpResponseMessage OkExerciseResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(FakeExerciseJson, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue;
        public int CallCount { get; private set; }

        public FakeHandler(params HttpResponseMessage[] responses)
            => _queue = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(
                _queue.Count > 0
                    ? _queue.Dequeue()
                    : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
