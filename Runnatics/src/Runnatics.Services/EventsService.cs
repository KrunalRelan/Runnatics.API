using AutoMapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Client.Responses.Events;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services
{
    public class EventsService(IUnitOfWork<RaceSyncDbContext> repository,
                                IMapper mapper,
                                ILogger<AuthenticationService> logger,
                                IConfiguration configuration) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IEventsService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger _logger = logger;
        protected readonly IConfiguration _configuration = configuration;

        public async Task<PagingList<EventResponse>> Search(EventSearchRequest request)
        {
            var toReturn = new PagingList<EventResponse>();

            var eventRepo = _repository.GetRepository<Event>();

            Expression<Func<Event, bool>> expression = e =>
                (string.IsNullOrEmpty(request.Name) || e.Name == request.Name) &&
                (string.IsNullOrEmpty(request.Status) || e.Status == request.Status) &&
                (!request.EventDateFrom.HasValue || e.EventDate >= request.EventDateFrom.Value) &&
                (!request.EventDateTo.HasValue || e.EventDate <= request.EventDateTo.Value);

            var data = await eventRepo.SearchAsync(expression,
                                                    request.PageSize,
                                                    request.PageNumber,
                                                    request.SortDirection == SortDirection.Ascending ?
                                                                       Models.Data.Common.SortDirection.Ascending : 
                                                                       Models.Data.Common.SortDirection.Descending,
                                                    false,
                                                    request.SortFieldName,
                                                    false
            );

            var mappedData = _mapper.Map<PagingList<EventResponse>>(data); //Todo
            return mappedData;
        }

        public async Task<EventResponse> Create(EventRequest request)
        {
            var toReturn = default(EventResponse);

            try
            {
                var eventRepo = _repository.GetRepository<Event>();

                // Check for existing event with same name and date, and is active/not deleted
                Expression<Func<Event, bool>> expression = e =>
                    e.Name == request.Name &&
                    e.EventDate == request.EventDate &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted;

                var alreadyExists = eventRepo.GetQuery(expression);

                if (alreadyExists != null)
                {
                    this.ErrorMessage = "Event already exists with the same name and date.";
                    return toReturn;
                }

                // Map request to entity
                var eventEntity = _mapper.Map<Event>(request);

                // Set audit properties if needed
                eventEntity.AuditProperties.IsActive = true;
                eventEntity.AuditProperties.IsDeleted = false;
                eventEntity.AuditProperties.CreatedDate = DateTime.UtcNow;
                eventEntity.AuditProperties.CreatedBy = Guid.NewGuid();

                // Add and save
                await eventRepo.AddAsync(eventEntity);
                await _repository.SaveChangesAsync();

                // Map to response
                toReturn = _mapper.Map<EventResponse>(eventEntity); 
                return toReturn;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
                _logger.LogError(ex, this.ErrorMessage);
            }

            return toReturn;
        }
    }
}
