using OpenFlux.Zen.Server.Services;

namespace OpenFlux.Zen.Server.Web.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

        group.MapGet("/stats", async (ISystemStatsService statsService) =>
        {
            var stats = await statsService.GetStatsAsync();
            return Results.Ok(stats);
        });

        return app;
    }
}
