using OpenFlux.Zen.Server.Services;

namespace OpenFlux.Zen.Server.Middleware;

public sealed class SecretPathMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecretPathMiddleware> _logger;

    public SecretPathMiddleware(RequestDelegate next, ILogger<SecretPathMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ISettingsService settingsService, IAuthService authService)
    {
        var settings = await settingsService.GetSettingsAsync();
        var secretPrefix = "/" + settings.SecretPath.Trim('/');

        var path = context.Request.Path.Value ?? "/";

        // Check if request starts with the secret prefix
        if (!path.Equals(secretPrefix, StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(secretPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            // Silently deny access to avoid disclosing the existence of the panel
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Not Found");
            return;
        }

        // Exact match on /{secretPath} without trailing slash -> redirect to /{secretPath}/
        if (path.Equals(secretPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : "";
            context.Response.Redirect(secretPrefix + "/" + queryString, permanent: false);
            return;
        }

        // Strip secret prefix into PathBase so downstream middleware (static files, routing, controllers)
        // work naturally with relative URLs
        var remainder = path[secretPrefix.Length..];
        if (string.IsNullOrEmpty(remainder))
        {
            remainder = "/";
        }

        context.Request.PathBase = secretPrefix;
        context.Request.Path = remainder;

        // Perform authentication check for internal API routes
        if (remainder.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            var isPublicApi = remainder.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
                              remainder.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase) ||
                              remainder.Equals("/api/health", StringComparison.OrdinalIgnoreCase);

            if (!isPublicApi)
            {
                var token = ExtractToken(context);
                if (string.IsNullOrWhiteSpace(token) || !authService.ValidateToken(token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
                    return;
                }
            }
        }

        await _next(context);
    }

    public static string? ExtractToken(HttpContext context)
    {
        // 1. Check Authorization header
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var headerVal = authHeader.ToString();
            if (headerVal.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = headerVal["Bearer ".Length..].Trim();
                if (!string.IsNullOrEmpty(token)) return token;
            }
        }

        // 2. Check Cookie
        if (context.Request.Cookies.TryGetValue("zen_auth_token", out var cookieToken))
        {
            if (!string.IsNullOrEmpty(cookieToken)) return cookieToken;
        }

        // 3. Check Query parameter
        if (context.Request.Query.TryGetValue("token", out var queryToken))
        {
            if (!string.IsNullOrEmpty(queryToken)) return queryToken.ToString();
        }

        return null;
    }
}
