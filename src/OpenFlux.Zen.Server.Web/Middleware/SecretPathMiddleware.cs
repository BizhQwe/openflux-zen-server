using OpenFlux.Zen.Server.Common;
using OpenFlux.Zen.Server.Models;
using OpenFlux.Zen.Server.Services;
using OpenFlux.Zen.Server.Web.Services;

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
            // Decoy site handling (like in 3x-ui / x-ui-pro)
            await ServeDecoyAsync(context, settings);
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

    private static async Task ServeDecoyAsync(HttpContext context, AppSettings settings)
    {
        // 1. If external redirect URL is configured, redirect immediately
        if (!string.IsNullOrWhiteSpace(settings.DecoyRedirectUrl))
        {
            context.Response.Redirect(settings.DecoyRedirectUrl, permanent: false);
            return;
        }

        // 2. Resolve requested path relative to decoy root
        var reqPath = context.Request.Path.Value?.TrimStart('/') ?? "";
        if (reqPath.Contains("..") || Path.IsPathRooted(reqPath))
        {
            reqPath = "";
        }

        string? fileToServe = null;

        // Check custom user decoy directory: data/decoy/
        var customDecoyDir = AppPaths.GetCustomDecoyDirectory();
        if (Directory.Exists(customDecoyDir))
        {
            if (!string.IsNullOrEmpty(reqPath))
            {
                var candidate = Path.Combine(customDecoyDir, reqPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) fileToServe = candidate;
            }
            if (fileToServe == null && File.Exists(Path.Combine(customDecoyDir, "index.html")))
            {
                fileToServe = Path.Combine(customDecoyDir, "index.html");
            }
        }

        // Check built-in decoy directory: wwwroot/decoy/
        if (fileToServe == null)
        {
            var builtInDecoyDir = AppPaths.GetBuiltInDecoyDirectory();
            if (Directory.Exists(builtInDecoyDir))
            {
                if (!string.IsNullOrEmpty(reqPath))
                {
                    var candidate = Path.Combine(builtInDecoyDir, reqPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate)) fileToServe = candidate;
                }
                if (fileToServe == null && File.Exists(Path.Combine(builtInDecoyDir, "index.html")))
                {
                    fileToServe = Path.Combine(builtInDecoyDir, "index.html");
                }
            }
        }

        if (fileToServe != null && File.Exists(fileToServe))
        {
            var ext = Path.GetExtension(fileToServe).ToLowerInvariant();
            var contentType = GetContentType(ext);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = contentType;
            if (ext == ".html" || ext == ".htm")
            {
                context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            }
            else
            {
                context.Response.Headers.Append("Cache-Control", "public, max-age=86400");
            }
            await context.Response.SendFileAsync(fileToServe);
            return;
        }

        // 3. Fallback: serve built-in HTML string
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsync(DecoySiteFallback.GetHtml());
    }

    private static string GetContentType(string ext) => ext switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };
}
