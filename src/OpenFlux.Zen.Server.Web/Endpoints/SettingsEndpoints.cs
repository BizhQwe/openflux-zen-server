using OpenFlux.Zen.Server.Models;
using OpenFlux.Zen.Server.Services;

namespace OpenFlux.Zen.Server.Web.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/settings", async (ISettingsService settingsService) =>
        {
            var s = await settingsService.GetSettingsAsync();
            return Results.Ok(s);
        });

        group.MapPost("/settings", async (AppSettings settings, ISettingsService settingsService) =>
        {
            var updated = await settingsService.UpdateSettingsAsync(settings);
            return Results.Ok(updated);
        });

        group.MapPost("/settings/regenerate-secret", async (ISettingsService settingsService) =>
        {
            var newSecret = await settingsService.RegenerateSecretPathAsync();
            return Results.Ok(new { secretPath = newSecret });
        });

        group.MapPost("/settings/autostart", async (AutostartRequest req, ISettingsService settingsService) =>
        {
            var ok = await settingsService.SetAutoStartAsync(req.Enabled);
            return Results.Ok(new { success = ok, autoStartEnabled = req.Enabled });
        });

        group.MapGet("/config/export", async (IExportImportService exportImport) =>
        {
            var json = await exportImport.ExportConfigurationJsonAsync();
            return Results.Content(json, "application/json; charset=utf-8");
        });

        group.MapPost("/config/import", async (HttpContext context, IExportImportService exportImport) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            var (imported, errors, message) = await exportImport.ImportConfigurationJsonAsync(json);
            return Results.Ok(new { imported, errors, message });
        });

        return app;
    }
}
