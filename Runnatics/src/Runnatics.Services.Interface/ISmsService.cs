namespace Runnatics.Services.Interface
{
    public interface ISmsService
    {
        Task<bool> SendSmsAsync(string phoneNumber, string message);
        Task<bool> SendTemplateSmsAsync(string phoneNumber, string templateId, Dictionary<string, string> variables);
    }
}
