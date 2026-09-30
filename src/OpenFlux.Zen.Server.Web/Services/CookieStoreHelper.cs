using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenFlux.Zen.Server.Services;

public static class CookieStoreHelper
{
    private static readonly HashSet<string> IgnoredCookieAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "path", "domain", "expires", "max-age", "secure", "httponly", "samesite", "priority", "version"
    };

    /// <summary>
    /// Intelligently parses cookies from any format: HTTP Cookie header, JSON, Netscape cookies.txt, or a raw token.
    /// </summary>
    public static Dictionary<string, string> ParseCookies(string? rawInput)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return result;
        }

        var trimmed = rawInput.Trim();

        // 1. JSON Object: { "spravka": "...", "yandexuid": "..." }
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(trimmed);
                if (dict != null && dict.Count > 0)
                {
                    foreach (var kvp in dict)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Key) && !IgnoredCookieAttributes.Contains(kvp.Key))
                        {
                            result[kvp.Key.Trim()] = kvp.Value?.Trim() ?? "";
                        }
                    }
                    if (result.Count > 0) return result;
                }
            }
            catch { }
        }

        // 2. JSON Array of cookie objects (e.g. from Cookie-Editor / EditThisCookie export):
        // [ { "name": "spravka", "value": "..." } ]
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (el.TryGetProperty("name", out var n) && el.TryGetProperty("value", out var v))
                        {
                            var nameStr = n.GetString()?.Trim();
                            var valStr = v.GetString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(nameStr) && !IgnoredCookieAttributes.Contains(nameStr))
                            {
                                result[nameStr] = valStr;
                            }
                        }
                    }
                    if (result.Count > 0) return result;
                }
            }
            catch { }
        }

        // 3. Netscape cookies.txt format (tab-delimited lines)
        if (trimmed.Contains('\t') && (trimmed.Contains(".yandex.ru") || trimmed.Contains("yandex.")))
        {
            var lines = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var l = line.Trim();
                if (string.IsNullOrWhiteSpace(l) || (l.StartsWith('#') && !l.StartsWith("#HttpOnly_")))
                {
                    continue;
                }
                var cols = l.Split('\t');
                if (cols.Length >= 7)
                {
                    var cookieName = cols[5].Trim();
                    var cookieVal = cols[6].Trim();
                    if (!string.IsNullOrEmpty(cookieName) && !IgnoredCookieAttributes.Contains(cookieName))
                    {
                        result[cookieName] = cookieVal;
                    }
                }
            }
            if (result.Count > 0) return result;
        }

        // 4. Standard HTTP Cookie Header: "name1=val1; name2=val2" or separated by newlines
        if (trimmed.Contains('=') || trimmed.Contains(';'))
        {
            var parts = trimmed.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;

                int eqIdx = p.IndexOf('=');
                if (eqIdx > 0)
                {
                    var key = p.Substring(0, eqIdx).Trim();
                    var val = p.Substring(eqIdx + 1).Trim();

                    // Strip surrounding quotes
                    if (val.StartsWith('"') && val.EndsWith('"') && val.Length >= 2)
                    {
                        val = val.Substring(1, val.Length - 2).Trim();
                    }

                    if (!string.IsNullOrEmpty(key) && !IgnoredCookieAttributes.Contains(key))
                    {
                        result[key] = val;
                    }
                }
            }
            if (result.Count > 0) return result;
        }

        // 5. Fallback: single raw token (e.g. user just copied the raw spravka value)
        if (trimmed.Length >= 10 && !trimmed.Contains(' ') && !trimmed.Contains('&'))
        {
            result["spravka"] = trimmed;
            return result;
        }

        return result;
    }

    /// <summary>
    /// Saves the cookies to the corresponding cookies-{transport}.json file for persistent reuse across restarts.
    /// </summary>
    public static async Task<bool> SaveCookiesToStoreFileAsync(
        string transport,
        string docUrl,
        Dictionary<string, string> cookies,
        string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(transport) || cookies == null || cookies.Count == 0)
        {
            return false;
        }

        var dataDir = Path.Combine(baseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        var filePath = Path.Combine(dataDir, $"cookies-{transport.Trim().ToLowerInvariant()}.json");

        Dictionary<string, Dictionary<string, string>> storeData = new(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(filePath))
        {
            try
            {
                var existingJson = await File.ReadAllTextAsync(filePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(existingJson);
                if (loaded != null)
                {
                    storeData = new Dictionary<string, Dictionary<string, string>>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
        }

        var key = string.IsNullOrWhiteSpace(docUrl) ? "default" : docUrl.Trim();
        if (!storeData.TryGetValue(key, out var existingJar) || existingJar == null)
        {
            existingJar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            storeData[key] = existingJar;
        }

        foreach (var kvp in cookies)
        {
            existingJar[kvp.Key] = kvp.Value;
        }

        // Also if docUrl has query params, save under base URL as well for resilient lookup
        if (key.Contains('?'))
        {
            var baseUrl = key.Substring(0, key.IndexOf('?')).Trim();
            if (!string.IsNullOrEmpty(baseUrl))
            {
                if (!storeData.TryGetValue(baseUrl, out var baseJar) || baseJar == null)
                {
                    baseJar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    storeData[baseUrl] = baseJar;
                }
                foreach (var kvp in cookies)
                {
                    baseJar[kvp.Key] = kvp.Value;
                }
            }
        }

        var tmpPath = filePath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(storeData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, filePath, overwrite: true);
            return true;
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            return false;
        }
    }
}
