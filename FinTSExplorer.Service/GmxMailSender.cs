using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace FinTSExplorer.Service;

public class GmxMailSender
{
    private const string SmtpHost = "mail.gmx.net";
    private const int SmtpPort = 587;
    private const string RecipientAddress = "andreaszoeller_2010@gmx.de";

    private readonly ILogger _logger;

    public GmxMailSender(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> SendAsync(MailContext context, string subject, string body)
    {
        // MailConfig.Load hat SenderAddress/SenderPassword bereits als nicht-leer validiert.
        var senderAddress = context.SenderAddress!;
        var senderPassword = context.SenderPassword!;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(context.SenderName, senderAddress));
        message.To.Add(new MailboxAddress("", RecipientAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(SmtpHost, SmtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(senderAddress, senderPassword);
            await client.SendAsync(message);
            _logger.LogInformation("Update-Mail versendet.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update-Mail konnte nicht versendet werden.");
            return false;
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true);
        }
    }
}
