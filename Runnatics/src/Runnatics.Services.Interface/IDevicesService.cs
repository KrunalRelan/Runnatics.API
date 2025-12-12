using Runnatics.Models.Client.Responses;

namespace Runnatics.Services.Interface
{
    public interface IDevicesService : ISimpleServiceBase
    {
        Task<List<DevicesResponse>> GetAllDevices();

        Task<bool> Create(string name);

        Task<bool> Update(string deviceId, string name);

        Task<bool> Delete(string deviceId);

        Task<DevicesResponse> GetDevice(string deviceId);
    }
}

