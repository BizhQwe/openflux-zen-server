using OpenFlux.Zen.Server.Services;

namespace OpenFlux.Zen.Server.Web.Endpoints;

public static class LogEndpoints
{
    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/tunnels/{id:guid}/logs", (Guid id, HttpContext context, ITunnelLogService logService) =>
        {
            int tail = 200;
            if (context.Request.Query.TryGetValue("tail", out var tailVal) && int.TryParse(tailVal, out var parsed))
            {
                tail = Math.Min(parsed, 1000);
            }
            var logs = logService.GetRecentLogs(id, tail);
            return Results.Ok(logs);
        });

        group.MapDelete("/tunnels/{id:guid}/logs", (Guid id, ITunnelLogService logService) =>
        {
            logService.ClearLogs(id);
            return Results.NoContent();
        });

        group.MapGet("/logs/system", (HttpContext context, ITunnelLogService logService) =>
        {
            int tail = 200;
            if (context.Request.Query.TryGetValue("tail", out var tailVal) && int.TryParse(tailVal, out var parsed))
            {
                tail = Math.Min(parsed, 1000);
            }
            var logs = logService.GetSystemLogs(tail);
            return Results.Ok(logs);
        });

        return app;
    }
}
