using System.Net;
using System.Net.Mail;
using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Shared.Services;

/// <summary>What came of trying to send.</summary>
/// <param name="Detail">
/// The server's own words on failure. Kept verbatim because Google's SMTP rejections name
/// the cause exactly — a wrong app password, an account without two-factor, a sign-in
/// blocked as suspicious — and paraphrasing them loses the only useful part.
/// </param>
public sealed record EmailResult(bool Succeeded, string Detail)
{
    public static EmailResult Ok(string detail) => new(true, detail);

    public static EmailResult Failed(string detail) => new(false, detail);
}

/// <summary>
/// Sends one email through the clinic's own SMTP account.
/// </summary>
/// <remarks>
/// The first thing in this app that talks to the network. Everything else is offline by
/// design; this is not, because email cannot be. It is called only where a person asked
/// for a message to go out.
/// </remarks>
public interface IEmailSender
{
    Task<EmailResult> SendAsync(
        NotificationSettings settings,
        string toAddress,
        string subject,
        string body,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IEmailSender"/>
public sealed class SmtpEmailSender : IEmailSender
{
    /// <summary>
    /// How long to wait on the server before giving up.
    /// </summary>
    /// <remarks>
    /// Twenty seconds. Long enough for a slow handshake on a clinic's connection, short
    /// enough that a wrong port — 465, where the TLS negotiation never completes — reports
    /// back rather than hanging the screen.
    /// </remarks>
    private const int TimeoutMilliseconds = 20_000;

    public async Task<EmailResult> SendAsync(
        NotificationSettings settings,
        string toAddress,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        if (!settings.IsConfigured)
        {
            return EmailResult.Failed(
                "No sending account is set up. Add the address and app password first.");
        }

        if (string.IsNullOrWhiteSpace(toAddress))
        {
            return EmailResult.Failed("There is no address to send to.");
        }

        // WebAssembly cannot open a socket, so SMTP is impossible there — no library
        // choice changes that. Reported as a refusal rather than left to throw, and the
        // guard is also what tells the platform analyser this code never runs in a
        // browser. A WASM head would have to post to a server that sends on its behalf.
        if (OperatingSystem.IsBrowser())
        {
            return EmailResult.Failed(
                "Email cannot be sent from a browser-hosted build — SMTP needs a socket. "
                    + "Use the server or desktop head.");
        }

        try
        {
            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = true,
                Timeout = TimeoutMilliseconds,

                // Explicit, because the default picks up machine-level credentials on
                // some hosts and then fails in a way that looks like a wrong password.
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(
                    settings.SenderAddress, settings.AppPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };

            using var message = new MailMessage
            {
                From = new MailAddress(
                    settings.SenderAddress!,
                    string.IsNullOrWhiteSpace(settings.SenderName)
                        ? settings.SenderAddress
                        : settings.SenderName),
                Subject = subject,
                Body = body,

                // Plain text. An HTML reminder has to be built and tested against a dozen
                // mail clients, and nothing here needs formatting yet.
                IsBodyHtml = false,
            };

            message.To.Add(toAddress);

            // A sending account is usually unmonitored, so a reply goes somewhere a person
            // reads instead.
            if (!string.IsNullOrWhiteSpace(settings.ReplyToAddress))
            {
                message.ReplyToList.Add(new MailAddress(settings.ReplyToAddress));
            }

            await client.SendMailAsync(message, ct).ConfigureAwait(false);

            return EmailResult.Ok($"Delivered to {settings.SmtpHost} for {toAddress}.");
        }
        catch (SmtpException ex)
        {
            // The status code is the actionable half — 535 is a bad app password, 534 is an
            // account that needs one, and they are the two failures a practice will hit.
            return EmailResult.Failed($"{ex.StatusCode}: {ex.Message}");
        }
        catch (FormatException ex)
        {
            return EmailResult.Failed($"That address is not valid: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            return EmailResult.Failed("The send was cancelled.");
        }
        catch (Exception ex)
        {
            // Everything else — no network, DNS failure, a firewall eating port 587.
            // Reported rather than swallowed: silence here reads as a message that went.
            return EmailResult.Failed(ex.Message);
        }
    }
}
