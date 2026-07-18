using Serilog.Context;

namespace NetIntercom.Api.Infrastructure;

/// <summary>Propagates X-Correlation-Id through the request, the response and the Serilog context.</summary>
public class CorrelationIdMiddleware
{
    public const string Header = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(Header, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : Guid.NewGuid().ToString("N");

        context.Response.Headers[Header] = correlationId;
        context.Items[Header] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
