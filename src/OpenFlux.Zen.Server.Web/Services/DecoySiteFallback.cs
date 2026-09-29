namespace OpenFlux.Zen.Server.Web.Services;

public static class DecoySiteFallback
{
    private static string? _cachedHtml;

    public static string GetHtml()
    {
        if (_cachedHtml != null) return _cachedHtml;

        // Try reading wwwroot/decoy/index.html first
        try
        {
            var p = Path.Combine(AppContext.BaseDirectory, "wwwroot", "decoy", "index.html");
            if (File.Exists(p))
            {
                _cachedHtml = File.ReadAllText(p);
                return _cachedHtml;
            }
        }
        catch { }

        // Robust built-in fallback
        _cachedHtml = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>Nexus Cloud Solutions — Enterprise Infrastructure</title>
  <style>
    :root { --bg: #0b0f19; --card: #131b2e; --text: #f1f5f9; --muted: #94a3b8; --primary: #3b82f6; }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: system-ui, -apple-system, sans-serif; background: var(--bg); color: var(--text); line-height: 1.6; padding: 40px 20px; }
    .container { max-width: 900px; margin: 0 auto; text-align: center; }
    h1 { font-size: 2.8rem; margin: 20px 0; color: #fff; }
    p { font-size: 1.2rem; color: var(--muted); margin-bottom: 30px; }
    .card { background: var(--card); border: 1px solid #1f2b48; border-radius: 12px; padding: 30px; margin-top: 40px; text-align: left; }
    .card h3 { color: var(--primary); margin-bottom: 10px; }
    .footer { margin-top: 60px; font-size: 0.85rem; color: var(--muted); }
  </style>
</head>
<body>
  <div class="container">
    <div style="font-size: 3rem; margin-bottom: 10px;">☁️</div>
    <h1>Nexus Cloud Solutions</h1>
    <p>High-performance distributed cloud infrastructure, microservices orchestration, and edge routing.</p>
    <div class="card">
      <h3>Enterprise Cloud Platform</h3>
      <p style="margin: 0;">Our global anycast network and zero-trust computing fabrics power mission-critical services worldwide with 99.8% SLA reliability.</p>
    </div>
    <div class="footer">&copy; 2026 Nexus Cloud Solutions AG. All rights reserved.</div>
  </div>
</body>
</html>
""";
        return _cachedHtml;
    }
}
