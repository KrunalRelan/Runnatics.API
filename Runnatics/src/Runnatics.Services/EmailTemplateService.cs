using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class EmailTemplateService : IEmailTemplateService
    {
        private const string Navy = "#1b2d5a";
        private const string Maroon = "#5a1a35";
        private const string Green = "#2d8e3f";

        public string BuildSupportQueryConfirmation(string submitterName, string subject, string ticketId)
        {
            var body = $@"
                <p>Hi {submitterName},</p>
                <p>Thank you for reaching out. We have received your support query and our team will get back to you shortly.</p>
                <table style=""width:100%;border-collapse:collapse;margin-top:16px;"">
                  <tr>
                    <td style=""padding:8px;background:#f5f5f5;font-weight:bold;width:30%;"">Ticket ID</td>
                    <td style=""padding:8px;background:#f5f5f5;"">#{ticketId}</td>
                  </tr>
                  <tr>
                    <td style=""padding:8px;font-weight:bold;"">Subject</td>
                    <td style=""padding:8px;"">{subject}</td>
                  </tr>
                </table>
                <p style=""margin-top:16px;"">We aim to respond within 24–48 hours on business days.</p>";

            return WrapInLayout("Support Query Received", body);
        }

        public string BuildSupportQueryReply(string submitterName, string subject, string replyBody)
        {
            var body = $@"
                <p>Hi {submitterName},</p>
                <p>Our support team has responded to your query: <strong>{subject}</strong></p>
                <div style=""border-left:4px solid {Navy};padding:12px 16px;margin:16px 0;background:#f9f9f9;"">
                  {replyBody}
                </div>
                <p>If you have further questions, please reply to this email or raise a new query.</p>";

            return WrapInLayout("Support Query Update", body);
        }

        public string BuildRegistrationConfirmation(string participantName, string eventName, string raceName, string bib, DateTime eventDate, string venueName)
        {
            var body = $@"
                <p>Hi {participantName},</p>
                <p>Your registration is confirmed. Here are your event details:</p>
                <table style=""width:100%;border-collapse:collapse;margin-top:16px;"">
                  <tr>
                    <td style=""padding:8px;background:#f5f5f5;font-weight:bold;width:35%;"">Event</td>
                    <td style=""padding:8px;background:#f5f5f5;"">{eventName}</td>
                  </tr>
                  <tr>
                    <td style=""padding:8px;font-weight:bold;"">Race</td>
                    <td style=""padding:8px;"">{raceName}</td>
                  </tr>
                  <tr>
                    <td style=""padding:8px;background:#f5f5f5;font-weight:bold;"">Bib Number</td>
                    <td style=""padding:8px;background:#f5f5f5;""><strong style=""font-size:18px;color:{Navy};"">{bib}</strong></td>
                  </tr>
                  <tr>
                    <td style=""padding:8px;font-weight:bold;"">Date</td>
                    <td style=""padding:8px;"">{eventDate:dddd, dd MMMM yyyy}</td>
                  </tr>
                  <tr>
                    <td style=""padding:8px;background:#f5f5f5;font-weight:bold;"">Venue</td>
                    <td style=""padding:8px;background:#f5f5f5;"">{venueName}</td>
                  </tr>
                </table>
                <p style=""margin-top:16px;"">We look forward to seeing you at the event. Good luck!</p>";

            return WrapInLayout("Registration Confirmed", body);
        }

        public string BuildRaceResultNotification(string participantName, string raceName, string gunTime, int overallRank, string status)
        {
            var rankColor = overallRank <= 3 ? Maroon : Navy;
            var body = $@"
                <p>Hi {participantName},</p>
                <p>Your results for <strong>{raceName}</strong> are now available.</p>
                <table style=""width:100%;border-collapse:collapse;margin-top:16px;"">
                  <tr>
                    <td style=""padding:8px;background:#f5f5f5;font-weight:bold;width:35%;"">Status</td>
                    <td style=""padding:8px;background:#f5f5f5;""><span style=""color:{Green};font-weight:bold;"">{status}</span></td>
                  </tr>
                  <tr>
                    <td style=""padding:8px;font-weight:bold;"">Gun Time</td>
                    <td style=""padding:8px;font-size:18px;font-weight:bold;"">{gunTime}</td>
                  </tr>
                  <tr>
                    <td style=""padding:8px;background:#f5f5f5;font-weight:bold;"">Overall Rank</td>
                    <td style=""padding:8px;background:#f5f5f5;font-size:18px;color:{rankColor};font-weight:bold;"">#{overallRank}</td>
                  </tr>
                </table>
                <p style=""margin-top:16px;"">Congratulations on completing the race!</p>";

            return WrapInLayout("Your Race Results", body);
        }

        private static string WrapInLayout(string title, string bodyContent)
        {
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{title}</title>
</head>
<body style=""margin:0;padding:0;background-color:#f4f4f4;font-family:Arial,Helvetica,sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#f4f4f4;"">
    <tr>
      <td align=""center"" style=""padding:24px 16px;"">
        <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#ffffff;border-radius:8px;overflow:hidden;"">

          <!-- Header -->
          <tr>
            <td style=""background-color:{Navy};padding:24px 32px;text-align:center;"">
              <span style=""color:#ffffff;font-size:26px;font-weight:bold;letter-spacing:2px;"">RACETIK</span>
            </td>
          </tr>

          <!-- Title bar -->
          <tr>
            <td style=""background-color:{Maroon};padding:12px 32px;"">
              <span style=""color:#ffffff;font-size:14px;font-weight:bold;text-transform:uppercase;letter-spacing:1px;"">{title}</span>
            </td>
          </tr>

          <!-- Body -->
          <tr>
            <td style=""padding:32px;color:#333333;font-size:15px;line-height:1.6;"">
              {bodyContent}
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""background-color:#f9f9f9;border-top:1px solid #e0e0e0;padding:20px 32px;text-align:center;"">
              <p style=""margin:0;font-size:12px;color:#888888;"">Racetik &mdash; Race Management Platform</p>
              <p style=""margin:4px 0 0;font-size:11px;color:#aaaaaa;"">You received this email because of your activity on Racetik. To unsubscribe, contact support.</p>
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
        }
    }
}
