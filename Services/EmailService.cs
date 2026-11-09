using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace Pauth40;

public sealed record SmtpOptions(
    string Host,
    int Port,
    bool UseSsl,
    string UserName,
    string Password,
    string From);

public static class SmtpDefaults
{
    /// <summary>Best-effort SMTP defaults for the common mail providers.</summary>
    public static SmtpOptions ForEmail(string email)
    {
        string address = (email ?? "").Trim();
        int at = address.LastIndexOf('@');
        string domain = at >= 0 ? address[(at + 1)..].ToLowerInvariant() : "";

        (string Host, int Port) = domain switch
        {
            "gmail.com" or "googlemail.com" => ("smtp.gmail.com", 587),
            "outlook.com" or "hotmail.com" or "live.com" or "msn.com" => ("smtp-mail.outlook.com", 587),
            "yahoo.com" or "ymail.com" => ("smtp.mail.yahoo.com", 587),
            "icloud.com" or "me.com" or "mac.com" => ("smtp.mail.me.com", 587),
            "zoho.com" => ("smtp.zoho.com", 587),
            "gmx.com" or "gmx.net" => ("smtp.gmx.com", 587),
            _ => ("", 587)
        };

        return new SmtpOptions(Host, Port, true, address, "", address);
    }
}

public sealed class EmailService
{
    public async Task SendAsync(
        SmtpOptions options,
        string to,
        string subject,
        string body)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
            throw new InvalidOperationException("O servidor SMTP não foi configurado.");

        if (!SecurityService.IsValidEmail(to))
            throw new InvalidOperationException("O e-mail de destino é inválido.");

        string from = string.IsNullOrWhiteSpace(options.From)
            ? options.UserName
            : options.From;

        if (!SecurityService.IsValidEmail(from))
            throw new InvalidOperationException("O remetente do e-mail é inválido.");

        using var message = new MailMessage(from, to.Trim(), subject, body)
        {
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };

        using var client = new SmtpClient(options.Host.Trim(), options.Port <= 0 ? 587 : options.Port)
        {
            EnableSsl = options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = 20_000
        };

        if (!string.IsNullOrWhiteSpace(options.UserName))
        {
            client.Credentials = new NetworkCredential(
                options.UserName.Trim(),
                options.Password ?? "");
        }

        try
        {
            await client.SendMailAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            throw new InvalidOperationException(
                "Não foi possível enviar o e-mail. Verifique o servidor, a porta e a senha de app. " +
                ex.Message,
                ex);
        }
    }

    public Task SendPasswordResetAsync(SmtpOptions options, string to, string code) =>
        SendAsync(
            options,
            to,
            "Pauth40 - código de redefinição de senha",
            $"""
            Seu código para redefinir a senha do Pauth40 é:

                {code}

            O código expira em 10 minutos. Se você não pediu esta redefinição,
            ignore esta mensagem.
            """);

    public Task SendTestAsync(SmtpOptions options, string to) =>
        SendAsync(
            options,
            to,
            "Pauth40 - teste de envio de e-mail",
            "Este é um teste das configurações de e-mail do Pauth40. " +
            "Se você recebeu esta mensagem, a recuperação de senha está pronta para uso.");
}
