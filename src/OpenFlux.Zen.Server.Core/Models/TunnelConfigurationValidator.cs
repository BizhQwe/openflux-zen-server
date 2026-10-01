namespace OpenFlux.Zen.Server.Models;

/// <summary>Validates the compact transport list before it reaches OpenFlux.</summary>
public static class TunnelConfigurationValidator
{
    private static readonly HashSet<string> KnownTransports = new(StringComparer.OrdinalIgnoreCase)
    {
        "direct", "yandex", "vyandex", "boards", "mailru", "cupsonline", "oneme"
    };

    public static string? Validate(Tunnel tunnel)
    {
        if (tunnel == null) return "Конфигурация туннеля не задана.";

        var transport = (tunnel.Transport ?? "yandex").Trim().ToLowerInvariant();
        if (transport != "multi") return null;

        if (string.IsNullOrWhiteSpace(tunnel.Transports)) return null; // supervisor supplies the documented default

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tunnel.Transports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = raw.Split(':', 2, StringSplitOptions.TrimEntries);
            var type = fields[0].ToLowerInvariant();
            if (!KnownTransports.Contains(type))
                return $"Неизвестный транспорт '{fields[0]}'.";
            if (!seen.Add(type))
                return $"Транспорт '{type}' указан несколько раз. Добавьте его одной карточкой.";
            if (fields.Length == 2 && (!int.TryParse(fields[1], out var priority) || priority is < 0 or > 1000))
                return $"Приоритет транспорта '{type}' должен быть целым числом от 0 до 1000.";
        }

        return seen.Count == 0 ? "Добавьте хотя бы один транспорт в multi-сессию." : null;
    }
}
