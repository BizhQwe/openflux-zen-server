namespace OpenFlux.Zen.Server.Models;

/// <summary>
/// Classification of the out-of-band cookie request sent by OpenFlux.
/// The core uses the same IPC message for a SmartCaptcha and for a login
/// wall, so the reason must be checked before the panel presents a captcha.
/// </summary>
public static class TunnelChallengeState
{
    public static bool IsCaptchaReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return false;
        var value = reason.Trim().ToLowerInvariant();
        return value.Contains("captcha", StringComparison.Ordinal) ||
               value.Contains("showcaptcha", StringComparison.Ordinal) ||
               value.Contains("human", StringComparison.Ordinal) ||
               value.Contains("anti-bot", StringComparison.Ordinal) ||
               value.Contains("antibot", StringComparison.Ordinal);
    }

    public static bool IsLoginReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return false;
        var value = reason.Trim().ToLowerInvariant();
        return value.Contains("login", StringComparison.Ordinal) ||
               value.Contains("auth", StringComparison.Ordinal) ||
               value.Contains("account", StringComparison.Ordinal) ||
               value.Contains("private", StringComparison.Ordinal);
    }

    public static bool HasPendingCaptcha(Tunnel tunnel) =>
        tunnel != null &&
        (!string.IsNullOrWhiteSpace(tunnel.PendingCaptchaChallengeUrl) ||
         (!string.IsNullOrWhiteSpace(tunnel.PendingCaptchaUrl) && IsCaptchaReason(tunnel.PendingCaptchaReason)));

    public static bool HasPendingAuth(Tunnel tunnel) =>
        tunnel != null && !string.IsNullOrWhiteSpace(tunnel.PendingCaptchaUrl) &&
        IsLoginReason(tunnel.PendingCaptchaReason);

    public static void Clear(Tunnel tunnel)
    {
        tunnel.PendingCaptchaUrl = null;
        tunnel.PendingCaptchaReason = null;
        tunnel.PendingCaptchaProxy = null;
        tunnel.PendingCaptchaRemote = false;
        tunnel.PendingCaptchaTransport = null;
        tunnel.PendingCaptchaChallengeUrl = null;
    }
}
