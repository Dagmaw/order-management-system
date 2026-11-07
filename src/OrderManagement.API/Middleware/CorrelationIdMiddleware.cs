using Serilog.Context;
using System.Diagnostics;

namespace OrderManagement.API.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Activity.Current?.Id
                            ?? context.TraceIdentifier;

        context.Items["CorrelationId"] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] =
                correlationId;

            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(
                   "CorrelationId",
                   correlationId))
        {
            await _next(context);
        }
    }
}