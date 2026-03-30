using System.Net;

namespace Paw.Polar;

public sealed class PolarApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Payload { get; }

    public PolarApiException(string message, HttpStatusCode statusCode, string payload, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Payload = payload;
    }
}

