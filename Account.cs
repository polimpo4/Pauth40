using System.Text.Json.Serialization;
using Pauth40.Localization;

namespace Pauth40;

public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Issuer { get; set; } = "";
    public string Label { get; set; } = "";
    public string Secret { get; set; } = "";
    public string Algorithm { get; set; } = "SHA1";
    public int Digits { get; set; } = 6;
    public int Period { get; set; } = 30;
    public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Issuer))
                return Issuer.Trim();
            if (!string.IsNullOrWhiteSpace(Label))
                return Label.Trim();
            return Loc.T("account_authenticator");
        }
    }

    [JsonIgnore]
    public string Subtitle
    {
        get
        {
            var label = Label.Trim();
            var name = Name;
            if (!string.IsNullOrWhiteSpace(label) &&
                !string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }

            return Loc.Format("account_subtitle", Digits, Period);
        }
    }

    [JsonIgnore]
    public string Initial
    {
        get
        {
            var name = Name.Trim();
            return name.Length == 0 ? "?" : name[..1].ToUpperInvariant();
        }
    }
}
