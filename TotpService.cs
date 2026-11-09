using OtpNet;

namespace Pauth40;

internal sealed class TotpService
{
    private readonly Totp _totp;

    public TotpService(Account account)
    {
        Account = account;
        _totp = new Totp(
            Base32Encoding.ToBytes(account.Secret),
            step: account.Period,
            mode: ParseAlgorithm(account.Algorithm),
            totpSize: account.Digits);
    }

    public Account Account { get; }

    public int Period => Account.Period;

    public long PeriodIndex => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Period;

    public string Compute()
    {
        return _totp.ComputeTotp().PadLeft(Account.Digits, '0');
    }

    public int RemainingSeconds
    {
        get
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return (int)(Period - (now % Period));
        }
    }

    public double Fraction
    {
        get
        {
            long periodMs = Period * 1000L;
            long elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % periodMs;
            return 1.0 - (elapsed / (double)periodMs);
        }
    }

    public static OtpHashMode ParseAlgorithm(string algorithm)
    {
        return algorithm.Replace("-", "").Trim().ToUpperInvariant() switch
        {
            "SHA256" => OtpHashMode.Sha256,
            "SHA512" => OtpHashMode.Sha512,
            _ => OtpHashMode.Sha1
        };
    }
}
