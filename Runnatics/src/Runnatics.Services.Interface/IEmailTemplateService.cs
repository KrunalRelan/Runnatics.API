namespace Runnatics.Services.Interface
{
    public interface IEmailTemplateService
    {
        string BuildSupportQueryConfirmation(string submitterName, string subject, string ticketId);
        string BuildSupportQueryReply(string submitterName, string subject, string replyBody);
        string BuildRegistrationConfirmation(string participantName, string eventName, string raceName, string bib, DateTime eventDate, string venueName);
        string BuildRaceResultNotification(string participantName, string raceName, string gunTime, int overallRank, string status);
    }
}
