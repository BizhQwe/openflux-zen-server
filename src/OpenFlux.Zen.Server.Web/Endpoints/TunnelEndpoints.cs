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

        group.MapGet("/{id:guid}/captcha", async (Guid id, HttpContext context, ITunnelManager manager) =>
        {
            var tunnel = await manager.GetByIdAsync(id);
            if (tunnel == null) return Results.NotFound();

            var hasPending = !string.IsNullOrWhiteSpace(tunnel.PendingCaptchaUrl);
            var targetUrl = !string.IsNullOrWhiteSpace(tunnel.PendingCaptchaUrl) ? tunnel.PendingCaptchaUrl : tunnel.Url;

            // If requested directly from browser (or with ?redirect=true), redirect directly to Yandex Disk!
            var accept = context.Request.Headers.Accept.ToString();
            var wantsHtml = accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
                            context.Request.Query.ContainsKey("redirect");

            if (wantsHtml && !string.IsNullOrWhiteSpace(targetUrl))
            {
                return Results.Redirect(targetUrl);
            }

            return Results.Ok(new
            {
                hasPendingCaptcha = hasPending,
                url = targetUrl,
                reason = tunnel.PendingCaptchaReason ?? "smartcaptcha",
                proxy = tunnel.PendingCaptchaProxy,
                documentUrl = tunnel.Url,
                errorMessage = tunnel.ErrorMessage
            });
        });

        group.MapMethods("/{id:guid}/cookies", new[] { "OPTIONS" }, (HttpContext ctx) =>
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
            ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/cookies", async (Guid id, HttpContext context, ITunnelManager manager) =>
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";

            string rawInput = "";
            if (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                using var reader = new StreamReader(context.Request.Body);
                var jsonBody = await reader.ReadToEndAsync();
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonBody);
                    if (doc.RootElement.TryGetProperty("cookies", out var c))
                    {
                        rawInput = c.GetString() ?? "";
                    }
                    else if (doc.RootElement.TryGetProperty("rawCookies", out var rc))
                    {
                        rawInput = rc.GetString() ?? "";
                    }
                    else
                    {
                        rawInput = jsonBody;
                    }
                }
                catch
                {
                    rawInput = jsonBody;
                }
            }
            else
            {
                using var reader = new StreamReader(context.Request.Body);
                rawInput = await reader.ReadToEndAsync();
            }

            var result = await manager.ApplyCookiesAsync(id, rawInput);
            if (!result.Success)
            {
                return Results.BadRequest(new { success = false, error = result.Message });
            }

            return Results.Ok(new { success = true, appliedCount = result.AppliedCount, message = result.Message });
        });

        return app;
    }
}
