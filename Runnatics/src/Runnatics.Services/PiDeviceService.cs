using Microsoft.EntityFrameworkCore;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Responses.PiDevice;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class PiDeviceService : SimpleServiceBase, IPiDeviceService
    {
        private readonly IUnitOfWork<RaceSyncDbContext> _unitOfWork;
        private readonly IEncryptionService _encryptionService;

        public PiDeviceService(
            IUnitOfWork<RaceSyncDbContext> unitOfWork,
            IEncryptionService encryptionService)
        {
            _unitOfWork = unitOfWork;
            _encryptionService = encryptionService;
        }

        public async Task<List<PiEventDto>?> GetActiveEventsWithRacesAsync(CancellationToken ct)
        {
            var today = DateTime.UtcNow.Date;

            var events = await _unitOfWork.GetRepository<Event>()
                .GetQuery()
                .AsNoTracking()
                .Include(e => e.Races)
                .Where(e => e.AuditProperties.IsActive
                         && !e.AuditProperties.IsDeleted
                         && e.EventDate >= today)
                .OrderBy(e => e.EventDate)
                .ToListAsync(ct);

            return events.Select(e => new PiEventDto
            {
                EncryptedId = _encryptionService.Encrypt(e.Id.ToString()),
                Name = e.Name,
                Races = e.Races
                    .Where(r => r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted)
                    .OrderBy(r => r.Id)
                    .Select(r => new PiRaceDto
                    {
                        EncryptedId = _encryptionService.Encrypt(r.Id.ToString()),
                        Name = r.Title
                    })
                    .ToList()
            }).ToList();
        }
    }
}
