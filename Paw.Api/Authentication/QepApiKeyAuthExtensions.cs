using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Paw.Api.Authentication;

public static class QepApiKeyAuthExtensions
{
    public static RouteHandlerBuilder RequireQepApiKey(this RouteHandlerBuilder builder, params string[] allowedRoles)
    {
        return builder.AddEndpointFilterFactory((context, next) =>
        {
            var logger = context.ApplicationServices.GetRequiredService<ILogger<QepApiKeyAuthFilter>>();
            var filter = new QepApiKeyAuthFilter(allowedRoles, logger);
            return invocationContext => filter.InvokeAsync(invocationContext, next);
        });
    }
}
