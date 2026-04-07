using Serilog.Context;

namespace Paw.Api.Middleware;

/// <summary>
/// Reads X-Correlation-ID from the request (or generates a new one), pushes it
/// onto Serilog's LogContext so every log line for the request includes it, and
/// echoes it back in the response header.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
