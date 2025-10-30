using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services.Interface
{
    public interface IEventsService : ISimpleServiceBase
    {
        Task CreateEventAsync(EventRequest request);
    }
}
