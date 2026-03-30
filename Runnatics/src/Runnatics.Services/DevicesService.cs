using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Devices;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class DevicesService(
        IUnitOfWork<RaceSyncDbContext> repository,
        IMapper mapper,
        ILogger<DevicesService> logger,
        IConfiguration configuration,
        IUserContextService userContext,
        IEncryptionService encryptionService) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IDevicesService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger<DevicesService> _logger = logger;
        protected readonly IConfiguration _configuration = configuration;
        protected readonly IUserContextService _userContext = userContext;
        private readonly IEncryptionService _encryptionService = encryptionService;

        public async Task<bool> Create(DeviceRequest request)
        {
            try
            {
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var deviceRepo = _repository.GetRepository<Device>();

                var existingDevice = await deviceRepo
                    .GetQuery(e => e.Name == request.Name
                        && !e.AuditProperties.IsDeleted
                        && e.AuditProperties.IsActive)
                    .FirstOrDefaultAsync();

                if (existingDevice != null)
                {
                    ErrorMessage = "Device already exists.";
                    _logger.LogError("Device already exists with name: {Name}", request.Name);
                    return false;
                }

                var createDevice = _mapper.Map<Device>(request);
                createDevice.TenantId = tenantId;
                createDevice.AuditProperties = new AuditProperties
                {
                    CreatedBy = userId,
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true,
                    IsDeleted = false
                };

                await deviceRepo.AddAsync(createDevice);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Device created for tenant: {TenantId} by user: {UserId}",
                    createDevice.TenantId, userId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating device for tenant: {TenantId}", _userContext.TenantId);
                ErrorMessage = "Error creating device.";
                return false;
            }
        }

        public async Task<bool> Delete(string deviceId)
        {
            try
            {
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var deviceRepo = _repository.GetRepository<Device>();

                var decryptedDeviceId = Convert.ToInt32(_encryptionService.Decrypt(deviceId));

                var existing = await deviceRepo.GetQuery(
                                    d => d.Id == decryptedDeviceId &&
                                    d.TenantId == tenantId &&
                                    d.AuditProperties.IsActive &&
                                    !d.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (existing == null)
                {
                    ErrorMessage = "Device not found or does not belong to this tenant.";
                    _logger.LogWarning("Device delete failed - not found. DeviceId: {DeviceId}, TenantId: {TenantId}", decryptedDeviceId, tenantId);
                    return false;
                }

                // Soft delete
                existing.AuditProperties.IsActive = false;
                existing.AuditProperties.IsDeleted = true;
                existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existing.AuditProperties.UpdatedBy = userId;

                await deviceRepo.UpdateAsync(existing);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Device deleted successfully. Id: {DeviceId}, TenantId: {TenantId}", decryptedDeviceId, tenantId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting device. DeviceId: {DeviceId}", deviceId);
                ErrorMessage = "Error deleting device.";
                return false;
            }
        }

        public async Task<List<DevicesResponse>> GetAllDevices()
        {
            try
            {
                var tenantId = _userContext.TenantId;

                var deviceRepo = _repository.GetRepository<Device>();
                var list = await deviceRepo.GetQuery(
                        d => d.TenantId == tenantId &&
                        d.AuditProperties.IsActive &&
                        !d.AuditProperties.IsDeleted)
                    .ToListAsync();

                return _mapper.Map<List<DevicesResponse>>(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving devices for tenant: {TenantId}", _userContext.TenantId);
                ErrorMessage = "Error retrieving devices.";
                return [];
            }
        }

        public async Task<DevicesResponse> GetDevice(string deviceId)
        {
            try
            {
                var tenantId = _userContext.TenantId;

                var decryptedDeviceId = Convert.ToInt32(_encryptionService.Decrypt(deviceId));

                var deviceRepo = _repository.GetRepository<Device>();
                var existing = await deviceRepo.GetQuery(
                            d => d.Id == decryptedDeviceId &&
                            d.TenantId == tenantId &&
                            d.AuditProperties.IsActive &&
                            !d.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (existing == null)
                {
                    ErrorMessage = "Device not found.";
                    return null!;
                }

                return _mapper.Map<DevicesResponse>(existing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving device {DeviceId}", deviceId);
                ErrorMessage = "Error retrieving device.";
                return null!;
            }
        }

        public async Task<bool> Update(string deviceId, DeviceRequest request)
        {
            try
            {
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var deviceRepo = _repository.GetRepository<Device>();

                var decryptedDeviceId = Convert.ToInt32(_encryptionService.Decrypt(deviceId));

                var existing = await deviceRepo.GetQuery(
                                    d => d.Id == decryptedDeviceId &&
                                    d.TenantId == tenantId &&
                                    d.AuditProperties.IsActive &&
                                    !d.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (existing == null)
                {
                    ErrorMessage = "Device not found.";
                    _logger.LogWarning("Device update failed - not found. DeviceId: {DeviceId}, TenantId: {TenantId}", decryptedDeviceId, tenantId);
                    return false;
                }

                existing.Name = request.Name;
                existing.DeviceMacAddress = request.DeviceMacAddress;
                existing.Hostname = request.Hostname;
                existing.IpAddress = request.IpAddress;
                existing.FirmwareVersion = request.FirmwareVersion;
                existing.ReaderModel = request.ReaderModel;
                existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existing.AuditProperties.UpdatedBy = userId;

                await deviceRepo.UpdateAsync(existing);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Device updated successfully. Id: {DeviceId}, TenantId: {TenantId}", decryptedDeviceId, tenantId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating device. DeviceId: {DeviceId}", deviceId);
                ErrorMessage = "Error updating device.";
                return false;
            }
        }
    }
}
