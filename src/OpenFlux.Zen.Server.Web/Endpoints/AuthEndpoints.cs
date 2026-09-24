using OpenFlux.Zen.Server.Middleware;
using OpenFlux.Zen.Server.Models;
using OpenFlux.Zen.Server.Services;

namespace OpenFlux.Zen.Server.Web.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapPost("/auth/login", async (LoginRequest req, IAuthService auth, HttpContext context) =>
        {
            var (success, token, username) = await auth.LoginAsync(req.Username, req.Password);
            if (!success)
            {
                return Results.Json(new LoginResponse { Success = false, Message = "Invalid username or password" }, statusCode: 401);
            }

            var isHttps = context.Request.IsHttps ||
                          string.Equals(context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);

            context.Response.Cookies.Append("zen_auth_token", token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Secure = isHttps,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });

            return Results.Ok(new LoginResponse { Success = true, Token = token, Username = username });
        });

        group.MapPost("/auth/logout", (IAuthService auth, HttpContext context) =>
        {
            var token = SecretPathMiddleware.ExtractToken(context);
            if (!string.IsNullOrWhiteSpace(token))
            {
                auth.RevokeToken(token);
            }
            context.Response.Cookies.Delete("zen_auth_token", new CookieOptions { Path = "/" });
            return Results.Ok(new { success = true });
        });

        group.MapGet("/auth/me", async (ISettingsService settingsService) =>
        {
            var s = await settingsService.GetSettingsAsync();
            return Results.Ok(new { username = s.Username, secretPath = s.SecretPath, publicUrl = s.PublicUrl });
        });

        group.MapPost("/auth/change-profile", async (ChangePasswordRequest req, IAuthService auth) =>
        {
            var (success, msg, newUsername) = await auth.ChangeProfileAsync(req.CurrentPassword, req.NewUsername, req.NewPassword);
            return success ? Results.Ok(new { success = true, message = msg, username = newUsername }) : Results.BadRequest(new { error = msg });
        });

        group.MapPost("/auth/change-password", async (ChangePasswordRequest req, IAuthService auth) =>
        {
            var (success, msg, newUsername) = await auth.ChangeProfileAsync(req.CurrentPassword, req.NewUsername, req.NewPassword);
            return success ? Results.Ok(new { success = true, message = msg, username = newUsername }) : Results.BadRequest(new { error = msg });
        });

        group.MapGet("/credentials", async (IAuthService auth) =>
        {
            var creds = await auth.GetCredentialsAsync();
            return Results.Ok(creds);
        });

        return app;
    }
}
