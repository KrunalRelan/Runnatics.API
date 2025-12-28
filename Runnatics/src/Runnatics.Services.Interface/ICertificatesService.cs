using Runnatics.Models.Client.Requests.Certificates;
using Runnatics.Models.Client.Responses.Certificates;

namespace Runnatics.Services.Interface
{
    public interface ICertificatesService : ISimpleServiceBase
    {
        Task<CertificateTemplateResponse?> CreateTemplateAsync(CertificateTemplateRequest request);
        Task<CertificateTemplateResponse?> UpdateTemplateAsync(string id, CertificateTemplateRequest request);
        Task<CertificateTemplateResponse?> GetTemplateAsync(string id);
        Task<List<CertificateTemplateResponse>> GetTemplatesByEventAsync(string eventId);
        Task<CertificateTemplateResponse?> GetTemplateByRaceAsync(string eventId, string raceId);
        Task<bool> DeleteTemplateAsync(string id);
    }
}
