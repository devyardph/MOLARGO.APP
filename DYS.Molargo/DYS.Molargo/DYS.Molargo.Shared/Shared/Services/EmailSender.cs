using System.Net;
using System.Net.Mail;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

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
    /// <summary>
    /// Sends one message, and records that it went — or did not. Never throws.
    /// </summary>
    /// <param name="purpose">
    /// What the message was for, in the words the audit trail should use — "Appointment
    /// reminder", "Sign-in code". Optional, and worth passing wherever it is known: an
    /// entry reading "Email sent to j@example.com" answers half the question somebody is
    /// asking, and the half it leaves out is the one they came for.
    /// </param>
    /// <param name="patientId">
    /// Set where the message was about a patient, so it lands on their access history
    /// alongside everything else that touched the record.
    /// </param>
    Task<EmailResult> SendAsync(
        NotificationSettings settings,
        string toAddress,
        string subject,
        string body,
        CancellationToken ct = default,
        string? purpose = null,
        Guid? patientId = null);
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

    private readonly IAuditLog _audit;

    /// <remarks>
    /// The audit writer is the only reason this has a constructor at all — the sending
    /// itself is stateless and takes its account per call. It is also why the registration
    /// moved from a singleton to scoped: the log stamps the acting person, and a singleton
    /// holding a scoped writer would file every clinic's mail under whoever signed in first.
    /// </remarks>
    public SmtpEmailSender(IAuditLog audit)
    {
        _audit = audit;
    }

    /// <remarks>
    /// The audit write wraps the send rather than being repeated inside it. Every exit
    /// below is a fact worth recording — including the refusals, because from the
    /// recipient's side a mail account nobody configured is indistinguishable from a server
    /// rejecting, and the log is where somebody goes to find out which it was — and one
    /// wrapper cannot miss the branch somebody adds next.
    /// </remarks>
    public async Task<EmailResult> SendAsync(
        NotificationSettings settings,
        string toAddress,
        string subject,
        string body,
        CancellationToken ct = default,
        string? purpose = null,
        Guid? patientId = null)
    {
        var result = await TrySendAsync(settings, toAddress, subject, body, ct)
            .ConfigureAwait(false);

        await RecordAsync(result, toAddress, subject, purpose, patientId)
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Records what happened, without ever being the reason a send reports failure.
    /// </summary>
    /// <remarks>
    /// Swallowed on purpose, like the rest of <see cref="IAuditLog"/>'s contract: the
    /// message has already gone by the time this runs, and an audit row that cannot be
    /// written must not turn a delivered reminder into an error somebody reads as "not
    /// sent". It takes no cancellation token for the same reason — a caller giving up on
    /// the send is not a reason to lose the record that it happened.
    /// </remarks>
    private async Task RecordAsync(
        EmailResult result,
        string toAddress,
        string subject,
        string? purpose,
        Guid? patientId)
    {
        var what = string.IsNullOrWhiteSpace(purpose) ? "Email" : $"{purpose} email";

        var detail = result.Succeeded
            ? $"{what} sent to {Recipient(toAddress)} — \"{subject}\""
            : $"{what} to {Recipient(toAddress)} failed — \"{subject}\": {result.Detail}";

        try
        {
            await _audit.RecordAsync(
                result.Succeeded
                    ? AuditAction.NotificationSent
                    : AuditAction.NotificationFailed,
                nameof(NotificationSettings),
                entityId: null,
                detail,
                patientId)
                .ConfigureAwait(false);
        }
        catch
        {
            // Nothing to escalate to. The caller is told what the send did, which is the
            // part it can act on.
        }
    }

    /// <summary>The address, or a stand-in where there was not one.</summary>
    private static string Recipient(string toAddress) =>
        string.IsNullOrWhiteSpace(toAddress) ? "no address" : toAddress.Trim();

    private static async Task<EmailResult> TrySendAsync(
        NotificationSettings settings,
        string toAddress,
        string subject,
        string body,
        CancellationToken ct)
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
