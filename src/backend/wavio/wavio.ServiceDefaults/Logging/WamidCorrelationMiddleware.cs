using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace wavio.ServiceDefaults.Logging;

/// <summary>
/// Correlation for the wamid chain (spec §3.2: correlation ID = wamid chain end-to-end).
///
/// Every log line inside a request scope carries:
///   • <c>CorrelationId</c> — from the <c>X-Correlation-Id</c> request header when present
///     (the YARP gateway and service-to-service calls forward it), else newly generated.
///     Echoed back on the response so callers can chain.
///   • <c>Wamid</c> — from the <c>X-Wamid</c> header when the request is part of a message
///     chain (send acks, status webhooks, replays). Wave 1 services also push the wamid via
///     <see cref="BeginWamidScope"/> the moment they learn it (e.g. after a Graph send
///     returns the id, or while processing one webhook entry).
///
/// wa_id is NEVER used as a correlation value — it is PII (spec §5); wamid is not.
/// </summary>
public sealed class WamidCorrelationMiddleware(RequestDelegate next)
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string WamidHeader = "X-Wamid";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = Guid.NewGuid().ToString("N");

        context.Response.Headers[CorrelationHeader] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            var wamid = context.Request.Headers[WamidHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(wamid))
            {
                await next(context);
                return;
            }

            using (LogContext.PushProperty("Wamid", wamid))
                await next(context);
        }
    }
}

public static class WamidCorrelationExtensions
{
    /// <summary>
    /// Adds wamid-chain correlation. Register early — right after forwarded headers — so
    /// every downstream log line (auth failures included) carries the correlation id.
    /// </summary>
    public static WebApplication UseWamidCorrelation(this WebApplication app)
    {
        app.UseMiddleware<WamidCorrelationMiddleware>();
        return app;
    }

    /// <summary>
    /// Pushes a wamid into the log context from non-HTTP execution paths (bus consumers,
    /// outbox relay, background jobs). Dispose the returned scope when the unit of work ends.
    /// </summary>
    public static IDisposable BeginWamidScope(string wamid) =>
        LogContext.PushProperty("Wamid", wamid);
}
