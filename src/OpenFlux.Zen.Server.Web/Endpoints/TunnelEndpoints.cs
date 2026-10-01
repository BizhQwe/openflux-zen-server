using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using OpenFlux.Zen.Server.Middleware;
using OpenFlux.Zen.Server.Models;
using OpenFlux.Zen.Server.Services;

namespace OpenFlux.Zen.Server.Web.Endpoints;

public static class TunnelEndpoints
{
    private static readonly ConcurrentDictionary<string, CookieContainer> _captchaSessions = new();

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
            var validationError = TunnelConfigurationValidator.Validate(tunnel);
            if (validationError != null) return Results.BadRequest(new { error = validationError });
            var created = await manager.CreateAsync(tunnel);
            return Results.Created($"/api/tunnels/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (Guid id, Tunnel tunnel, ITunnelManager manager) =>
        {
            tunnel.Id = id;
            var validationError = TunnelConfigurationValidator.Validate(tunnel);
            if (validationError != null) return Results.BadRequest(new { error = validationError });
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

            var hasPending = TunnelChallengeState.HasPendingCaptcha(tunnel);
            var hasPendingAuth = TunnelChallengeState.HasPendingAuth(tunnel);
            // Always start from the document URL. OpenFlux Android does the
            // same: /showcaptcha URLs are one-use redirect targets and return
            // Yandex 400 when opened without the core's redirect cookie jar.
            var targetUrl = !string.IsNullOrWhiteSpace(tunnel.PendingCaptchaUrl) &&
                            !tunnel.PendingCaptchaUrl.Contains("/showcaptcha", StringComparison.OrdinalIgnoreCase)
                ? tunnel.PendingCaptchaUrl
                : tunnel.Url;

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
                hasPendingAuth,
                url = targetUrl,
                reason = tunnel.PendingCaptchaReason,
                proxy = tunnel.PendingCaptchaProxy,
                remote = tunnel.PendingCaptchaRemote,
                transport = tunnel.PendingCaptchaTransport ?? tunnel.Transport,
                challengeUrl = tunnel.PendingCaptchaChallengeUrl,
                documentUrl = tunnel.Url,
                errorMessage = tunnel.ErrorMessage
            });
        });

        group.MapGet("/{id:guid}/captcha/view", async (Guid id, HttpContext context, ITunnelManager manager) =>
        {
            var tunnel = await manager.GetByIdAsync(id);
            if (tunnel == null) return Results.NotFound();
            if (!TunnelChallengeState.HasPendingCaptcha(tunnel) && !TunnelChallengeState.HasPendingAuth(tunnel))
            {
                return Results.Conflict(new { error = "OpenFlux ещё не запросил проверку или вход для этого туннеля." });
            }

            var targetUrl = !string.IsNullOrWhiteSpace(tunnel.PendingCaptchaUrl) &&
                            !tunnel.PendingCaptchaUrl.Contains("/showcaptcha", StringComparison.OrdinalIgnoreCase)
                ? tunnel.PendingCaptchaUrl
                : tunnel.Url;

            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                return Results.Content(@"<!DOCTYPE html>
<html><head><meta charset='utf-8'><style>body{background:#0f172a;color:#f87171;font-family:sans-serif;text-align:center;padding:40px;}</style></head>
<body><h3>URL документа не настроен в туннеле.</h3></body></html>", "text/html; charset=utf-8");
            }

            var cookieContainer = new CookieContainer();
            var requestedTransport = context.Request.Query["transport"].ToString();
            _captchaSessions[CaptchaSessionKey(id, tunnel, requestedTransport)] = cookieContainer;

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                UseCookies = true,
                CookieContainer = cookieContainer,
                Proxy = CreateProxy(tunnel.PendingCaptchaProxy),
                UseProxy = !string.IsNullOrWhiteSpace(tunnel.PendingCaptchaProxy)
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);

            var ua = context.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(ua))
            {
                ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            }
            client.DefaultRequestHeaders.Add("User-Agent", ua);
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            if (Uri.TryCreate(tunnel.Url, UriKind.Absolute, out var documentUri))
            {
                client.DefaultRequestHeaders.Referrer = documentUri;
            }

            HttpResponseMessage resp;
            try
            {
                resp = await client.GetAsync(targetUrl);
            }
            catch (Exception ex)
            {
                return Results.Content($"<!DOCTYPE html><html><head><meta charset='utf-8'><style>body{{background:#0f172a;color:#f87171;font-family:sans-serif;padding:30px;}}</style></head><body><h3>Не удалось связаться с сервисом:</h3><p>{WebUtility.HtmlEncode(ex.Message)}</p></body></html>", "text/html; charset=utf-8");
            }

            var finalUri = resp.RequestMessage?.RequestUri ?? new Uri(targetUrl);
            var originHost = finalUri.Host;
            var html = await resp.Content.ReadAsStringAsync();

            // A Yandex document can become available without showing a challenge (for
            // example after the IP was already verified).  In that case the response
            // contains the access cookie and must be handed to OpenFlux immediately.
            // Without this check the panel renders the normal document inside the
            // solver iframe and leaves the tunnel in the captcha state forever.
            var initialJar = CollectCookieJar(cookieContainer, tunnel.Url, finalUri.ToString());
            if (!ContainsCaptchaChallenge(html) && initialJar.Count > 0)
            {
                var applied = await manager.ApplyCookiesAsync(id, System.Text.Json.JsonSerializer.Serialize(initialJar), requestedTransport);
                if (applied.Success)
                {
                    _captchaSessions.TryRemove(CaptchaSessionKey(id, tunnel, requestedTransport), out _);
                    return Results.Content(GetCookiesAppliedHtml(applied.AppliedViaIpc), "text/html; charset=utf-8");
                }

                return Results.Content(GetErrorHtml("Кука доступа получена, но не удалось применить её к туннелю. Повторите запуск туннеля."), "text/html; charset=utf-8");
            }

            var token = SecretPathMiddleware.ExtractToken(context);
            return RenderCaptchaHtml(html, originHost, id, context, token, requestedTransport);
        });

        group.MapPost("/{id:guid}/captcha/submit", async (Guid id, HttpContext context, ITunnelManager manager) =>
        {
            var tunnel = await manager.GetByIdAsync(id);
            if (tunnel == null) return Results.NotFound();
            if (!TunnelChallengeState.HasPendingCaptcha(tunnel) && !TunnelChallengeState.HasPendingAuth(tunnel))
            {
                return Results.Conflict(new { error = "OpenFlux ещё не запросил проверку или вход для этого туннеля." });
            }

            var targetAction = context.Request.Query["target"].ToString();
            var origin = context.Request.Query["origin"].ToString();
            var requestedTransport = context.Request.Query["transport"].ToString();
            if (string.IsNullOrWhiteSpace(targetAction))
            {
                return Results.BadRequest(new { error = "Missing target" });
            }

            var form = await context.Request.ReadFormAsync();
            var formDict = new Dictionary<string, string>();
            foreach (var key in form.Keys)
            {
                formDict[key] = form[key].ToString();
            }

            if (!_captchaSessions.TryGetValue(CaptchaSessionKey(id, tunnel, requestedTransport), out var cookieContainer))
            {
                cookieContainer = new CookieContainer();
            }

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = cookieContainer,
                Proxy = CreateProxy(tunnel.PendingCaptchaProxy),
                UseProxy = !string.IsNullOrWhiteSpace(tunnel.PendingCaptchaProxy)
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(25);

            var ua = context.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(ua))
            {
                ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            }
            client.DefaultRequestHeaders.Add("User-Agent", ua);
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            if (!string.IsNullOrEmpty(origin))
            {
                client.DefaultRequestHeaders.Add("Origin", origin);
                client.DefaultRequestHeaders.Add("Referer", targetAction);
            }

            var content = new FormUrlEncodedContent(formDict);
            HttpResponseMessage resp;
            try
            {
                resp = await client.PostAsync(targetAction, content);
            }
            catch (Exception ex)
            {
                return Results.Content($"<!DOCTYPE html><html><head><meta charset='utf-8'><style>body{{background:#0f172a;color:#f87171;font-family:sans-serif;padding:30px;}}</style></head><body><h3>Ошибка отправки решения:</h3><p>{WebUtility.HtmlEncode(ex.Message)}</p></body></html>", "text/html; charset=utf-8");
            }

            var token = SecretPathMiddleware.ExtractToken(context);
            var spravkaVal = ExtractSpravkaCookie(resp, cookieContainer, targetAction);

            if (!string.IsNullOrWhiteSpace(spravkaVal))
            {
                var jar = CollectCookieJar(cookieContainer, tunnel.Url, targetAction);
                jar["spravka"] = spravkaVal;
                var applied = await manager.ApplyCookiesAsync(id, System.Text.Json.JsonSerializer.Serialize(jar), requestedTransport);
                if (applied.Success)
                {
                    _captchaSessions.TryRemove(CaptchaSessionKey(id, tunnel, requestedTransport), out _);
                    return Results.Content(GetCookiesAppliedHtml(applied.AppliedViaIpc), "text/html; charset=utf-8");
                }

                return Results.Content(GetErrorHtml("Решение получено, но куку доступа не удалось передать туннелю."), "text/html; charset=utf-8");
            }

            // Handle redirect if Yandex responded with 3xx to next step
            if ((int)resp.StatusCode >= 300 && (int)resp.StatusCode < 400 && resp.Headers.Location != null)
            {
                var targetUri = new Uri(targetAction);
                var loc = resp.Headers.Location;
                var redirectUri = loc.IsAbsoluteUri ? loc : new Uri(new Uri($"{targetUri.Scheme}://{targetUri.Host}"), loc);

                try
                {
                    var redResp = await client.GetAsync(redirectUri);
                    var redSpravka = ExtractSpravkaCookie(redResp, cookieContainer, redirectUri.ToString());
                    if (!string.IsNullOrWhiteSpace(redSpravka))
                    {
                        var jar = CollectCookieJar(cookieContainer, tunnel.Url, redirectUri.ToString());
                        jar["spravka"] = redSpravka;
                        var applied = await manager.ApplyCookiesAsync(id, System.Text.Json.JsonSerializer.Serialize(jar), requestedTransport);
                        if (applied.Success)
                        {
                            _captchaSessions.TryRemove(CaptchaSessionKey(id, tunnel, requestedTransport), out _);
                            return Results.Content(GetCookiesAppliedHtml(applied.AppliedViaIpc), "text/html; charset=utf-8");
                        }

                        return Results.Content(GetErrorHtml("Решение получено, но куку доступа не удалось передать туннелю."), "text/html; charset=utf-8");
                    }

                    var redHtml = await redResp.Content.ReadAsStringAsync();
                    var redJar = CollectCookieJar(cookieContainer, tunnel.Url, redirectUri.ToString());
                    if (!ContainsCaptchaChallenge(redHtml) && redJar.Count > 0)
                    {
                        var applied = await manager.ApplyCookiesAsync(id, System.Text.Json.JsonSerializer.Serialize(redJar), requestedTransport);
                        if (applied.Success)
                        {
                            _captchaSessions.TryRemove(CaptchaSessionKey(id, tunnel, requestedTransport), out _);
                            return Results.Content(GetCookiesAppliedHtml(applied.AppliedViaIpc), "text/html; charset=utf-8");
                        }
                    }
                    return RenderCaptchaHtml(redHtml, redirectUri.Host, id, context, token, requestedTransport);
                }
                catch
                {
                    // fallback to view redirect
                }
            }

            // If 200 OK, Yandex likely returned the second step (picture challenge / puzzle)
            if (resp.IsSuccessStatusCode)
            {
                var stepHtml = await resp.Content.ReadAsStringAsync();
                var stepJar = CollectCookieJar(cookieContainer, tunnel.Url, targetAction);
                if (!ContainsCaptchaChallenge(stepHtml) && stepJar.Count > 0)
                {
                    var applied = await manager.ApplyCookiesAsync(id, System.Text.Json.JsonSerializer.Serialize(stepJar), requestedTransport);
                    if (applied.Success)
                    {
                        _captchaSessions.TryRemove(CaptchaSessionKey(id, tunnel, requestedTransport), out _);
                        return Results.Content(GetCookiesAppliedHtml(applied.AppliedViaIpc), "text/html; charset=utf-8");
                    }
                }
                var host = new Uri(targetAction).Host;
                return RenderCaptchaHtml(stepHtml, host, id, context, token, requestedTransport);
            }

            // Fallback: redirect back to view so user can try again
            var viewUrl = $"{context.Request.PathBase}/api/tunnels/{id}/captcha/view";
            if (!string.IsNullOrEmpty(token))
            {
                viewUrl += $"?token={Uri.EscapeDataString(token)}";
            }
            return Results.Redirect(viewUrl);
        });

        group.MapPost("/{id:guid}/quick-update-url", async (Guid id, HttpContext context, ITunnelManager manager) =>
        {
            var tunnel = await manager.GetByIdAsync(id);
            if (tunnel == null) return Results.NotFound();

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            string newUrl = "";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("url", out var u))
                {
                    newUrl = u.GetString() ?? "";
                }
            }
            catch
            {
                newUrl = body.Trim();
            }

            if (string.IsNullOrWhiteSpace(newUrl))
            {
                return Results.BadRequest(new { error = "Укажите ссылку на документ" });
            }

            tunnel.Url = newUrl.Trim();
            TunnelChallengeState.Clear(tunnel);
            tunnel.ErrorMessage = null;
            await manager.UpdateAsync(tunnel);

            await manager.StopAsync(id);
            await Task.Delay(300);
            await manager.StartAsync(id);

            return Results.Ok(new { success = true, url = tunnel.Url });
        });

        group.MapPost("/{id:guid}/switch-to-direct", async (Guid id, ITunnelManager manager) =>
        {
            var tunnel = await manager.GetByIdAsync(id);
            if (tunnel == null) return Results.NotFound();

            tunnel.Transport = "direct";
            TunnelChallengeState.Clear(tunnel);
            tunnel.ErrorMessage = null;
            await manager.UpdateAsync(tunnel);

            await manager.StopAsync(id);
            await Task.Delay(300);
            await manager.StartAsync(id);

            return Results.Ok(new { success = true, transport = "direct" });
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

    private static string ExtractSpravkaCookie(HttpResponseMessage resp, CookieContainer cookieContainer, string targetUriStr)
    {
        if (resp.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            foreach (var header in cookieHeaders)
            {
                var match = Regex.Match(header, @"(?i)spravka=([^;]+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }

        try
        {
            var uri = new Uri(targetUriStr);
            var containerCookies = cookieContainer.GetCookies(uri);
            if (containerCookies["spravka"] != null)
            {
                return containerCookies["spravka"]!.Value;
            }
        }
        catch { }

        foreach (Cookie c in cookieContainer.GetAllCookies())
        {
            if (string.Equals(c.Name, "spravka", StringComparison.OrdinalIgnoreCase))
            {
                return c.Value;
            }
        }

        return string.Empty;
    }

    private static Dictionary<string, string> CollectCookieJar(CookieContainer container, params string?[] urls)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Cookie cookie in container.GetAllCookies())
        {
            if (!cookie.Expired && !string.IsNullOrWhiteSpace(cookie.Name))
            {
                result[cookie.Name] = cookie.Value;
            }
        }

        // GetAllCookies is available on current .NET, while these explicit
        // URLs also cover jars supplied by alternate CookieContainer builds.
        foreach (var raw in urls)
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) continue;
            foreach (Cookie cookie in container.GetCookies(uri))
            {
                if (!cookie.Expired && !string.IsNullOrWhiteSpace(cookie.Name))
                {
                    result[cookie.Name] = cookie.Value;
                }
            }
        }

        return result;
    }

    private static string CaptchaSessionKey(Guid id, Tunnel tunnel, string? requestedTransport = null)
    {
        var transport = string.IsNullOrWhiteSpace(requestedTransport)
            ? (tunnel.PendingCaptchaTransport ?? tunnel.Transport)
            : requestedTransport;
        return $"{id:N}:{transport.Trim().ToLowerInvariant()}";
    }

    private static bool ContainsCaptchaChallenge(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return false;

        // Keep this list specific to challenge pages.  A generic "captcha" search
        // also matches analytics and help text on ordinary Yandex documents.
        return Regex.IsMatch(
            html,
            @"(?i)(showcaptcha|smartcaptcha|smart-captcha|checkbox-captcha|captcha__challenge|captcha-container|captcha-challenge)",
            RegexOptions.CultureInvariant);
    }

    private static IWebProxy? CreateProxy(string? proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy)) return null;

        var value = proxy.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? new WebProxy(uri)
            : null;
    }

    private static string GetErrorHtml(string message) => $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'><style>
body {{ background:#0f172a; color:#f87171; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif; display:flex; align-items:center; justify-content:center; min-height:100vh; margin:0; padding:24px; text-align:center; }}
.card {{ background:#1e293b; border:1px solid #334155; border-radius:14px; padding:28px; max-width:480px; }}
p {{ color:#cbd5e1; line-height:1.5; }}
</style></head><body><div class='card'><h3>Не удалось применить проверку</h3><p>{WebUtility.HtmlEncode(message)}</p></div></body></html>";

    private static IResult RenderCaptchaHtml(string html, string originHost, Guid id, HttpContext context, string? token, string? transport)
    {
        // Find form action
        var formMatch = Regex.Match(html, @"(?i)<form[^>]*action=[""']([^""']+)[""']");
        var origAction = formMatch.Success ? formMatch.Groups[1].Value : "/checkcaptcha";
        if (!origAction.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !origAction.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!origAction.StartsWith("/")) origAction = "/" + origAction;
            origAction = $"https://{originHost}{origAction}";
        }

        var requestBase = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
        var tokenParam = !string.IsNullOrEmpty(token) ? $"&token={Uri.EscapeDataString(token)}" : "";
        var transportParam = !string.IsNullOrWhiteSpace(transport) ? $"&transport={Uri.EscapeDataString(transport)}" : "";
        var submitUrl = $"{requestBase}/api/tunnels/{id}/captcha/submit?target=" + Uri.EscapeDataString(origAction) + "&origin=" + Uri.EscapeDataString($"https://{originHost}") + transportParam + tokenParam;

        // Inject base href so all static assets, fonts, css, scripts load directly from origin
        html = Regex.Replace(html, @"(?i)<head>", $"<head><base href=\"https://{originHost}/\">", RegexOptions.IgnoreCase);

        // Replace form action with our absolute submitUrl
        html = Regex.Replace(html, @"(?i)(<form[^>]*action=)[""'][^""']*[""']", $"$1\"{submitUrl}\"", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"formAction:\s*""[^""]*""", $"formAction:\"{submitUrl}\"");

        // Inject form submit interceptor and message listener before </body>
        var injectedScript = $@"
<script>
  (function() {{
    var submitUrl = '{submitUrl}';
    function ensureSubmitUrl() {{
      var forms = document.getElementsByTagName('form');
      for (var i = 0; i < forms.length; i++) {{
        forms[i].action = submitUrl;
      }}
      if (window.__SSR_DATA__) {{
        window.__SSR_DATA__.formAction = submitUrl;
      }}
    }}
    document.addEventListener('DOMContentLoaded', ensureSubmitUrl);
    setInterval(ensureSubmitUrl, 200);
    window.addEventListener('submit', function(e) {{
      if (e.target && e.target.tagName === 'FORM') {{
        e.target.action = submitUrl;
      }}
    }}, true);
    window.addEventListener('message', function(e) {{
      if (e.data && e.data.type === 'openflux-captcha-solved') {{
        if (window.parent) window.parent.postMessage(e.data, '*');
      }}
    }});
  }})();
</script>
";
        html = html.Replace("</body>", injectedScript + "</body>");

        context.Response.Headers.Remove("X-Frame-Options");
        context.Response.Headers.Remove("Content-Security-Policy");

        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string GetCookiesAppliedHtml(bool appliedViaIpc) =>
        appliedViaIpc ? GetSuccessHtml() : GetCookiesSavedHtml();

    private static string GetCookiesSavedHtml() => @"<!DOCTYPE html>
<html><head><meta charset='utf-8'><style>
body{background:#0f172a;color:#cbd5e1;font-family:system-ui,sans-serif;text-align:center;padding:36px}
h3{color:#fbbf24}p{line-height:1.5}
</style></head><body><h3>Куки сохранены</h3><p>Ядро OpenFlux загрузит их при следующем подключении. Панель не получила подтверждение по IPC, поэтому статус туннеля нужно проверить после переподключения.</p></body></html>";
    private static string GetSuccessHtml() => @"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <style>
    body { background: #0f172a; color: #fff; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; text-align: center; }
    .card { background: #1e293b; padding: 32px; border-radius: 16px; border: 1px solid #334155; max-width: 420px; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }
    .icon { font-size: 48px; margin-bottom: 16px; }
    h2 { margin: 0 0 12px 0; color: #10b981; font-size: 1.4rem; }
    p { color: #94a3b8; font-size: 14px; margin: 0 0 20px 0; line-height: 1.5; }
    .btn { background: #2563eb; color: #fff; border: none; padding: 10px 20px; border-radius: 8px; font-weight: 600; cursor: pointer; }
  </style>
</head>
<body>
  <div class='card'>
    <div class='icon'>✅</div>
    <h2>Cookies переданы в OpenFlux</h2>
    <p>Куки сохранены и переданы ядру. Подождите переподключения туннеля: панель уберёт это сообщение только после подтверждения доступа самим OpenFlux.</p>
    <button class='btn' onclick='closeModal()'>Закрыть окно</button>
  </div>
  <script>
    function closeModal() {
      if (window.parent) {
        window.parent.postMessage({ type: 'openflux-captcha-waiting' }, '*');
      }
    }
    closeModal();
  </script>
</body>
</html>";
}
