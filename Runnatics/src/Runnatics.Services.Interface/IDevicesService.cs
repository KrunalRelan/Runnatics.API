using Runnatics.Models.Client.Requests.Devices;
using Runnatics.Models.Client.Responses;

namespace Runnatics.Services.Interface
{
    public interface IDevicesService : ISimpleServiceBase
    {
        Task<List<DevicesResponse>> GetAllDevices();

        Task<bool> Create(DeviceRequest request);

        Task<bool> Update(string deviceId, DeviceRequest request);

        Task<bool> Delete(string deviceId);

        Task<DevicesResponse> GetDevice(string deviceId);
    }
}

