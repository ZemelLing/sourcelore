using System.Text.RegularExpressions;
using LoreVcs;

namespace SourceLore.Infrastructure.Lore.Diagnostics;

public static partial class SafeDiagnostics
{
    public static string Describe(Exception error) => error is LoreError native
        ? $"Lore 操作失败（{native.ReturnCode}）：{Redact(native.Message)}"
        : Redact(error.Message);

    public static string Redact(string message)
    {
        var value = UrlCredentials().Replace(message, "$1[redacted]@");
        value = UrlQuery().Replace(value, "$1?[redacted]");
        return Credentials().Replace(value, "$1[redacted]");
    }

    [GeneratedRegex(@"([a-z][a-z0-9+.-]*://)[^\s/@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentials();
    [GeneratedRegex(@"([a-z][a-z0-9+.-]*://[^\s?]+)\?[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlQuery();
    [GeneratedRegex(@"((?:bearer\s+|(?:access[_-]?token|identity[_-]?token|token|password|authorization)\s*[:=]\s*(?:bearer\s+)?))[^\s,;]+", RegexOptions.IgnoreCase)]
    private static partial Regex Credentials();
}
