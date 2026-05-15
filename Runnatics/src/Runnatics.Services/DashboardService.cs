using AutoMapper;
using Azure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Responses.Dashboard;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class DashboardService(IUnitOfWork<RaceSyncDbContext> repository,
                               IMapper mapper,
                               ILogger<EventsService> logger,
                               IConfiguration configuration,
                               IUserContextService userContext,
                               IEncryptionService encryptionService) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IDashboardService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger _logger = logger;
        protected readonly IConfiguration _configuration = configuration;
        protected readonly IUserContextService _userContext = userContext;
        protected readonly IEncryptionService _encryptionService = encryptionService;

        public async Task<DashboardStatsResponse> GetDashboardStats()
        {
            try
            {
                var tenantId = _userContext.TenantId;

                var eventrepo = _repository.GetRepository<Event>();
                var totalEvents = await eventrepo.CountAsync(e => e.TenantId == tenantId && e.AuditProperties.IsActive 
                                                                                         && !e.AuditProperties.IsDeleted);
                var today = DateTime.UtcNow.Date;
                var tomorrow = today.AddDays(1);

                var totalActiveEvents = await eventrepo.CountAsync(e => e.TenantId == tenantId
                    && e.AuditProperties.IsActive
                    && !e.AuditProperties.IsDeleted
                    && e.EventDate >= today
                    && e.EventDate < tomorrow);

                var totalUpcomingEvents = await eventrepo.CountAsync(e => e.TenantId == tenantId
                    && e.AuditProperties.IsActive
                    && !e.AuditProperties.IsDeleted
                    && e.EventDate >= tomorrow);

                var totalcompletedEvents = await eventrepo.CountAsync(e => e.TenantId == tenantId
                    && e.AuditProperties.IsActive
                    && !e.AuditProperties.IsDeleted
                    && e.EventDate < today);

                var participantrepo = _repository.GetRepository<Participant>();
                var totalParticipants = await participantrepo.CountAsync(e => e.TenantId == tenantId);

                var response = new DashboardStatsResponse
                {
                    TotalEvents = totalEvents,
                    TotalParticipants = totalParticipants,
                    ActiveEvents = totalActiveEvents,
                    UpcomingEvents = totalUpcomingEvents,
                    CompletedEvents = totalcompletedEvents,
                };

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error fetching stats: {ex.Message}";
                _logger.LogError(ex, "Error fetching stats");
                return null!; // Null return is intentional on error - caller checks HasError
            }
        }

        public Task<List<RecentActivityItem>> GetRecentActivity(int limit)
        {
            throw new NotImplementedException();
        }

        public async Task<EventDashboardStatsDto?> GetEventDashboardStatsAsync(string eventId, CancellationToken ct)
        {
            try
            {
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var tenantId = _userContext.TenantId;

                var eventRepo = _repository.GetRepository<Event>();
                var eventEntity = await eventRepo.GetQuery(e =>
                    e.Id == decryptedEventId &&
                    e.TenantId == tenantId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (eventEntity == null)
                {
                    ErrorMessage = "Event not found";
                    return null;
                }

                var participantRepo = _repository.GetRepository<Participant>();
                var allParticipants = await participantRepo.GetQuery(p =>
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Select(p => new { p.Gender, p.AgeCategory, p.RaceId })
                    .ToListAsync(ct);

                var resultsRepo = _repository.GetRepository<Results>();
                var allResults = await resultsRepo.GetQuery(r =>
                    r.EventId == decryptedEventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Include(r => r.Participant)
                    .Include(r => r.Race)
                    .Select(r => new { r.Status, r.RaceId, r.Race.Title, r.Participant.Gender, r.Participant.AgeCategory })
                    .ToListAsync(ct);

                var raceRepo = _repository.GetRepository<Race>();
                var races = await raceRepo.GetQuery(r =>
                    r.EventId == decryptedEventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Select(r => new { r.Id, r.Title })
                    .ToListAsync(ct);

                var genderBreakdown = allParticipants
                    .GroupBy(p => p.Gender switch { "M" => "Male", "F" => "Female", var g => g ?? "Unknown" })
                    .Select(g => new GenderBreakdownItem
                    {
                        Gender = g.Key,
                        Count = g.Count(),
                        Finishers = allResults.Count(r => r.Status == "Finished" &&
                            (r.Gender switch { "M" => "Male", "F" => "Female", var x => x ?? "Unknown" }) == g.Key)
                    })
                    .OrderBy(g => g.Gender)
                    .ToList();

                var categoryBreakdown = allParticipants
                    .GroupBy(p => p.AgeCategory ?? "Unknown")
                    .Select(c => new CategoryBreakdownItem
                    {
                        Category = c.Key,
                        Count = c.Count(),
                        Finishers = allResults.Count(r => r.Status == "Finished" && (r.AgeCategory ?? "Unknown") == c.Key)
                    })
                    .OrderBy(c => c.Category)
                    .ToList();

                var raceStats = races.Select(race => new RaceStatItem
                {
                    RaceId = _encryptionService.Encrypt(race.Id.ToString()),
                    RaceName = race.Title,
                    Registered = allParticipants.Count(p => p.RaceId == race.Id),
                    Finishers = allResults.Count(r => r.RaceId == race.Id && r.Status == "Finished"),
                    Dnf = allResults.Count(r => r.RaceId == race.Id && r.Status == "DNF")
                }).ToList();

                return new EventDashboardStatsDto
                {
                    EventId = eventId,
                    EventName = eventEntity.Name,
                    TotalRegistered = allParticipants.Count,
                    TotalFinishers = allResults.Count(r => r.Status == "Finished"),
                    TotalDnf = allResults.Count(r => r.Status == "DNF"),
                    TotalDns = allResults.Count(r => r.Status == "DNS"),
                    GenderBreakdown = genderBreakdown,
                    CategoryBreakdown = categoryBreakdown,
                    RaceStats = raceStats
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error fetching event dashboard stats: {ex.Message}";
                _logger.LogError(ex, "Error in GetEventDashboardStatsAsync for event {EventId}", eventId);
                return null;
            }
        }

        public async Task<RaceDashboardStatsDto?> GetRaceDashboardStatsAsync(string eventId, string raceId, CancellationToken ct)
        {
            try
            {
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
                var tenantId = _userContext.TenantId;

                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r =>
                    r.Id == decryptedRaceId &&
                    r.EventId == decryptedEventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (race == null)
                {
                    ErrorMessage = "Race not found";
                    return null;
                }

                var participantRepo = _repository.GetRepository<Participant>();
                var participants = await participantRepo.GetQuery(p =>
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Select(p => new { p.Gender, p.AgeCategory })
                    .ToListAsync(ct);

                var resultsRepo = _repository.GetRepository<Results>();
                var results = await resultsRepo.GetQuery(r =>
                    r.RaceId == decryptedRaceId &&
                    r.EventId == decryptedEventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Include(r => r.Participant)
                    .Select(r => new { r.Status, r.NetTime, r.GunTime, r.Participant.Gender, r.Participant.AgeCategory })
                    .ToListAsync(ct);

                var finishers = results.Where(r => r.Status == "Finished").ToList();
                var finishTimes = finishers
                    .Select(r => r.NetTime ?? r.GunTime)
                    .Where(t => t.HasValue)
                    .Select(t => t!.Value)
                    .ToList();

                string? fastest = finishTimes.Count > 0
                    ? TimeSpan.FromMilliseconds(finishTimes.Min()).ToString(@"hh\:mm\:ss")
                    : null;
                string? average = finishTimes.Count > 0
                    ? TimeSpan.FromMilliseconds(finishTimes.Average()).ToString(@"hh\:mm\:ss")
                    : null;

                var genderBreakdown = participants
                    .GroupBy(p => p.Gender switch { "M" => "Male", "F" => "Female", var g => g ?? "Unknown" })
                    .Select(g => new GenderBreakdownItem
                    {
                        Gender = g.Key,
                        Count = g.Count(),
                        Finishers = finishers.Count(r =>
                            (r.Gender switch { "M" => "Male", "F" => "Female", var x => x ?? "Unknown" }) == g.Key)
                    })
                    .OrderBy(g => g.Gender)
                    .ToList();

                var categoryBreakdown = participants
                    .GroupBy(p => p.AgeCategory ?? "Unknown")
                    .Select(c => new CategoryBreakdownItem
                    {
                        Category = c.Key,
                        Count = c.Count(),
                        Finishers = finishers.Count(r => (r.AgeCategory ?? "Unknown") == c.Key)
                    })
                    .OrderBy(c => c.Category)
                    .ToList();

                return new RaceDashboardStatsDto
                {
                    RaceId = raceId,
                    RaceName = race.Title,
                    TotalRegistered = participants.Count,
                    TotalFinishers = finishers.Count,
                    TotalDnf = results.Count(r => r.Status == "DNF"),
                    TotalDns = results.Count(r => r.Status == "DNS"),
                    FastestFinishTime = fastest,
                    AverageFinishTime = average,
                    GenderBreakdown = genderBreakdown,
                    CategoryBreakdown = categoryBreakdown
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error fetching race dashboard stats: {ex.Message}";
                _logger.LogError(ex, "Error in GetRaceDashboardStatsAsync for race {RaceId}", raceId);
                return null;
            }
        }
    }
}
