using OtpNet;
using Pauth40.Localization;

namespace Pauth40;

internal static class OtpAuthParser
{
    public static bool TryParse(string text, out Account? account, out string error)
    {
        account = null;
        error = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            error = Loc.T("error_qr_empty");
            return false;
        }

        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
        {
            error = Loc.T("error_qr_uri");
            return false;
        }

        if (!string.Equals(uri.Scheme, "otpauth", StringComparison.OrdinalIgnoreCase))
        {
            error = Loc.T("error_qr_not_authenticator");
            return false;
        }

        var type = uri.Host.ToLowerInvariant();
        if (type == "hotp")
        {
            error = Loc.T("error_qr_hotp");
            return false;
        }
        if (type != "totp")
        {
            error = Loc.T("error_qr_type");
            return false;
        }

        var labelRaw = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Trim();
        var issuer = "";
        var label = labelRaw;

        int colon = labelRaw.IndexOf(':');
        if (colon >= 0)
        {
            issuer = labelRaw[..colon].Trim();
            label = labelRaw[(colon + 1)..].Trim();
        }

        var query = ParseQuery(uri.Query);

        if (query.TryGetValue("issuer", out var issuerParam) && !string.IsNullOrWhiteSpace(issuerParam))
            issuer = issuerParam.Trim();

        if (!query.TryGetValue("secret", out var secret) || string.IsNullOrWhiteSpace(secret))
        {
            error = Loc.T("error_qr_no_secret");
            return false;
        }

        secret = secret.Replace(" ", "").Trim().ToUpperInvariant();

        try
        {
            var key = Base32Encoding.ToBytes(secret);
            if (key.Length < 5)
            {
                error = Loc.T("error_qr_secret_short");
                return false;
            }
        }
        catch
        {
            error = Loc.T("error_qr_secret_base32");
            return false;
        }

        int digits = 6;
        if (query.TryGetValue("digits", out var digitsParam) &&
            int.TryParse(digitsParam, out var parsedDigits))
        {
            digits = Math.Clamp(parsedDigits, 6, 8);
        }

        int period = 30;
        if (query.TryGetValue("period", out var periodParam) &&
            int.TryParse(periodParam, out var parsedPeriod))
        {
            period = Math.Clamp(parsedPeriod, 10, 120);
        }

        var algorithm = "SHA1";
        if (query.TryGetValue("algorithm", out var algorithmParam))
        {
            var normalized = algorithmParam.Replace("-", "").Trim().ToUpperInvariant();
            if (normalized is "SHA1" or "SHA256" or "SHA512")
                algorithm = normalized;
        }

        if (string.IsNullOrWhiteSpace(issuer) && string.IsNullOrWhiteSpace(label))
        {
            issuer = Loc.T("account_authenticator");
            label = "";
        }

        account = new Account
        {
            Issuer = issuer,
            Label = label,
            Secret = secret,
            Algorithm = algorithm,
            Digits = digits,
            Period = period,
            AddedUtc = DateTimeOffset.UtcNow
        };

        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=');
            string key = equals >= 0 ? part[..equals] : part;
            string value = equals >= 0 ? part[(equals + 1)..] : "";

            key = Uri.UnescapeDataString(key.Replace("+", " "));
            value = Uri.UnescapeDataString(value.Replace("+", " "));
            result[key] = value;
        }

        return result;
    }
}
