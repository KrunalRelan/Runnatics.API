using AutoMapper;
using Azure;
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
                var totalEvents = await eventrepo.CountAsync(e => e.TenantId == tenantId);
                var totalActiveEvents = await eventrepo.CountAsync(e => e.TenantId == tenantId && e.AuditProperties.IsActive && e.EventDate > DateTime.UtcNow);
                var totalcompletedEvents = await eventrepo.CountAsync(e => e.TenantId == tenantId && 
                                                                           e.AuditProperties.IsActive && 
                                                                           e.EventDate < DateTime.UtcNow);

                var participantrepo = _repository.GetRepository<Participant>();
                var totalParticipants = await participantrepo.CountAsync(e => e.TenantId == tenantId);

                var response = new DashboardStatsResponse
                {
                    TotalEvents = totalEvents,
                    TotalParticipants = totalParticipants,
                    ActiveEvents = totalActiveEvents,
                    UpcomingEvents = totalActiveEvents,
                    CompletedEvents = totalcompletedEvents,
                };

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error fetching stats: {ex.Message}";
                _logger.LogError(ex, "Error fetching stats");
                return null;
            }
        }

        public Task<List<RecentActivityItem>> GetRecentActivity(int limit)
        {
            throw new NotImplementedException();
        }
    }
}
