using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Client.Responses.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services.Interface
{
    public interface IEventsService : ISimpleServiceBase
    {
        Task<PagingList<EventResponse>> Search(EventSearchRequest request);
        Task<EventResponse> Create(EventRequest request);
    }
}
