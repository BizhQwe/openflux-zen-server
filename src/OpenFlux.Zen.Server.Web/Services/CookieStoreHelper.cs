using System.Text.Json;
using System.Text.RegularExpressions;
using OpenFlux.Zen.Server.Common;

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

        // 3. Tab-delimited (Netscape cookies.txt OR Chrome/Edge/Firefox DevTools table row copy)
        if (trimmed.Contains('\t'))
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
                if (cols.Length >= 2)
                {
                    // Check if Netscape format (7 cols, first col is domain starting with . or http)
                    if (cols.Length >= 7 && (cols[0].StartsWith('.') || cols[0].Contains('.')) && (cols[1].Equals("TRUE", StringComparison.OrdinalIgnoreCase) || cols[1].Equals("FALSE", StringComparison.OrdinalIgnoreCase)))
                    {
                        var n = cols[5].Trim();
                        var v = cols[6].Trim();
                        if (!string.IsNullOrEmpty(n) && !IgnoredCookieAttributes.Contains(n)) result[n] = v;
                    }
                    else
                    {
                        // Chrome / Edge / Firefox DevTools Cookies Table copy: col 0 is Name, col 1 is Value
                        var n = cols[0].Trim();
                        var v = cols[1].Trim();
                        if (!string.IsNullOrEmpty(n) && !IgnoredCookieAttributes.Contains(n))
                        {
                            result[n] = v;
                        }
                    }
                }
            }
            if (result.Count > 0) return result;
        }

        // 4. Standard HTTP Cookie Header: "name1=val1; name2=val2" or separated by newlines or colons
        if (trimmed.Contains('=') || trimmed.Contains(';') || (trimmed.Contains(':') && (trimmed.Contains("spravka") || trimmed.Contains("yandex"))))
        {
            var parts = trimmed.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;

                int eqIdx = p.IndexOf('=');
                if (eqIdx < 0) eqIdx = p.IndexOf(':');

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
        string baseDirectory,
        string? alternateDocUrl = null)
    {
        if (string.IsNullOrWhiteSpace(transport) || cookies == null || cookies.Count == 0)
        {
            return false;
        }

        var transportClean = transport.Trim().ToLowerInvariant();
        var fileName = $"cookies-{transportClean}.json";

        var targetDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AppPaths.GetDataDirectory(),
            Path.Combine(baseDirectory, "data"),
            baseDirectory
        };

        bool anySuccess = false;

        foreach (var dataDir in targetDirs)
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                var filePath = Path.Combine(dataDir, fileName);

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

                var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "default" };
                if (!string.IsNullOrWhiteSpace(docUrl))
                {
                    var u = docUrl.Trim();
                    targetKeys.Add(u);
                    if (u.Contains('?')) targetKeys.Add(u.Substring(0, u.IndexOf('?')).Trim());
                }
                if (!string.IsNullOrWhiteSpace(alternateDocUrl))
                {
                    var a = alternateDocUrl.Trim();
                    targetKeys.Add(a);
                    if (a.Contains('?')) targetKeys.Add(a.Substring(0, a.IndexOf('?')).Trim());
                }

                // Also merge into all currently existing keys in storeData
                foreach (var existingKey in storeData.Keys.ToList())
                {
                    targetKeys.Add(existingKey);
                }

                foreach (var k in targetKeys)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    if (!storeData.TryGetValue(k, out var jar) || jar == null)
                    {
                        jar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        storeData[k] = jar;
                    }
                    foreach (var kvp in cookies)
                    {
                        jar[kvp.Key] = kvp.Value;
                    }
                }

                var tmpPath = filePath + ".tmp";
                var json = JsonSerializer.Serialize(storeData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(tmpPath, json);
                File.Move(tmpPath, filePath, overwrite: true);
                anySuccess = true;
            }
            catch { }
        }

        return anySuccess;
    }
}
