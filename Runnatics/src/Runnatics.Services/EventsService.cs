using AutoMapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public async Task CreateEventAsync(EventRequest request)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();

                var eventRequest = new Event
                {
                    Name = request.Details
                };

                await eventRepo.AddAsync(eventRequest);
                await _repository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
            }
        }

    }
}
