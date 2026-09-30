using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace FinTSExplorer.Service;

public class GmxMailSender
{
    private const string SmtpHost = "mail.gmx.net";
    private const int SmtpPort = 587;
    private const string RecipientAddress = "andreaszoeller_2010@gmx.de";
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger;

    public GmxMailSender(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> SendAsync(MailContext context, string subject, string body, CancellationToken cancellationToken = default)
    {
        // MailConfig.Load hat SenderAddress/SenderPassword bereits als nicht-leer validiert.
        var senderAddress = context.SenderAddress!;
        var senderPassword = context.SenderPassword!;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(context.SenderName, senderAddress));
        message.To.Add(new MailboxAddress("", RecipientAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var client = new SmtpClient();

            try
            {
                await client.ConnectAsync(SmtpHost, SmtpPort, SecureSocketOptions.StartTls, cancellationToken);
                await client.AuthenticateAsync(senderAddress, senderPassword, cancellationToken);
                await client.SendAsync(message, cancellationToken);
                _logger.LogInformation("Update-Mail versendet.");
                return true;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Update-Mail-Versand fehlgeschlagen (Versuch {Attempt}/{MaxAttempts}), erneuter Versuch in {Delay}.", attempt, MaxAttempts, RetryDelay);

                // Erst trennen, dann warten - sonst bleibt eine halb aufgebaute Verbindung waehrend der
                // gesamten Pause unnoetig offen (finally unten laeuft erst NACH diesem catch-Block).
                if (client.IsConnected)
                    await client.DisconnectAsync(true, cancellationToken);

                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update-Mail konnte nach {MaxAttempts} Versuchen nicht versendet werden.", MaxAttempts);
                return false;
            }
            finally
            {
                if (client.IsConnected)
                    await client.DisconnectAsync(true, cancellationToken);
            }
        }

        return false; // unreachable, aber vom Compiler benötigt
    }
}
