using OpenFlux.Zen.Server.Models;
using OpenFlux.Zen.Server.Services;

namespace OpenFlux.Zen.Server.Web.Endpoints;

public static class TunnelEndpoints
{
    public static IEndpointRouteBuilder MapTunnelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tunnels");

        group.MapGet("", async (ITunnelManager manager) =>
        {
            var tunnels = await manager.GetAllAsync();
            return Results.Ok(tunnels);
        });

        group.MapGet("/{id:guid}", async (Guid id, ITunnelManager manager) =>
        {
            var tunnel = await manager.GetByIdAsync(id);
            return tunnel != null ? Results.Ok(tunnel) : Results.NotFound();
        });

        group.MapPost("", async (Tunnel tunnel, ITunnelManager manager) =>
        {
            var created = await manager.CreateAsync(tunnel);
            return Results.Created($"/api/tunnels/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (Guid id, Tunnel tunnel, ITunnelManager manager) =>
        {
            tunnel.Id = id;
            var updated = await manager.UpdateAsync(tunnel);
            return updated != null ? Results.Ok(updated) : Results.NotFound();
        });

        group.MapDelete("/{id:guid}", async (Guid id, ITunnelManager manager) =>
        {
            var deleted = await manager.DeleteAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/{id:guid}/start", async (Guid id, ITunnelManager manager) =>
        {
            var started = await manager.StartAsync(id);
            return started ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "Failed to start tunnel" });
        });

        group.MapPost("/{id:guid}/stop", async (Guid id, ITunnelManager manager) =>
        {
            var stopped = await manager.StopAsync(id);
            return stopped ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "Failed to stop tunnel" });
        });

        group.MapPost("/{id:guid}/toggle", async (Guid id, HttpContext context, ITunnelManager manager) =>
        {
            bool enable = true;
            if (context.Request.Query.TryGetValue("enable", out var enableVal))
            {
                bool.TryParse(enableVal, out enable);
            }
            var success = await manager.ToggleEnableAsync(id, enable);
            return Results.Ok(new { success, isEnabled = enable });
        });

        group.MapPost("/{id:guid}/reset-stats", async (Guid id, ITunnelManager manager) =>
        {
            var success = await manager.ResetStatsAsync(id);
            return Results.Ok(new { success });
        });

        return app;
    }
}
