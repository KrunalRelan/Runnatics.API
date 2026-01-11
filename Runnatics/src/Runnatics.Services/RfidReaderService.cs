using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Reader;
using Runnatics.Models.Client.Responses.Reader;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class RfidReaderService(
        IUnitOfWork<RaceSyncDbContext> repository, 
        IMapper mapper,
        ILogger<RfidReaderService> logger) 
        : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IRfidReaderService
    {
        private readonly IMapper _mapper = mapper;
        private readonly ILogger<RfidReaderService> _logger = logger;

        // Cache for frequently accessed data
        private static Dictionary<string, int> _readerSerialCache = new();
        private static Dictionary<string, int> _epcToChipCache = new();
        private static DateTime _cacheLastRefresh = DateTime.MinValue;
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Process a single tag read from R700
        /// </summary>
        public async Task<TagReadResponse> ProcessTagReadAsync(TagReadRequest request)
        {
            try
            {
                var readerDeviceRepo = _repository.GetRepository<ReaderDevice>();
                var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
                var readRawRepo = _repository.GetRepository<ReadRaw>();
                var readerAssignmentRepo = _repository.GetRepository<ReaderAssignment>();

                // 1. Find reader by serial number
                var readerId = await GetReaderIdBySerialAsync(request.ReaderSerial);
                int? checkpointId = null;
                int eventId = 0;

                if (readerId.HasValue)
                {
                    var reader = await readerDeviceRepo.GetQuery(r => r.Id == readerId.Value)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();
                    
                    checkpointId = reader?.CheckpointId;
                    
                    // Get EventId from reader assignment
                    var activeAssignment = await readerAssignmentRepo.GetQuery(
                            ra => ra.ReaderDeviceId == readerId.Value && !ra.AuditProperties.IsDeleted && ra.IsActive)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();
                    eventId = activeAssignment?.EventId ?? 0;
                }

                // 2. Check for duplicate
                if (await IsDuplicateReadAsync(request.Epc, request.Timestamp, checkpointId))
                {
                    return new TagReadResponse
                    {
                        Success = true,
                        IsDuplicate = true,
                        Message = "Duplicate read ignored"
                    };
                }

                // 3. Find chip by EPC
                var chip = await GetChipByEpcAsync(request.Epc);

                // 4. Find participant if chip is assigned
                int? participantId = null;
                string? bibNumber = null;

                if (chip != null)
                {
                    var assignment = await chipAssignmentRepo.GetQuery(
                            ca => ca.ChipId == chip.Id && !ca.AuditProperties.IsDeleted,
                            includeNavigationProperties: true)
                        .AsNoTracking()
                        .Include(ca => ca.Participant)
                        .OrderByDescending(ca => ca.AuditProperties.CreatedDate)
                        .FirstOrDefaultAsync();

                    if (assignment != null)
                    {
                        participantId = assignment.ParticipantId;
                        bibNumber = assignment.Participant?.BibNumber;
                        
                        // Get EventId from chip assignment if not found from reader
                        if (eventId == 0)
                        {
                            eventId = assignment.EventId;
                        }
                    }
                }

                if (eventId == 0)
                {
                    _logger.LogWarning("Could not determine EventId for tag read EPC={Epc}", request.Epc);
                    return new TagReadResponse
                    {
                        Success = false,
                        Message = "Could not determine event for this read"
                    };
                }

                // 5. Create ReadRaw record using AutoMapper
                var readRaw = _mapper.Map<ReadRaw>(request);
                readRaw.EventId = eventId;
                readRaw.ReaderDeviceId = readerId ?? 0;
                readRaw.CheckpointId = checkpointId;
                readRaw.Source = "realtime";
                readRaw.IsProcessed = false;
                readRaw.AuditProperties = new AuditProperties
                {
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true,
                    IsDeleted = false
                };

                await readRawRepo.AddAsync(readRaw);
                await _repository.SaveChangesAsync();

                // 6. Update reader health status
                if (readerId.HasValue)
                {
                    await UpdateReaderLastReadAsync(readerId.Value);
                }

                _logger.LogDebug("Processed tag read: EPC={Epc}, Chip={ChipId}, Participant={ParticipantId}",
                    request.Epc, chip?.Id, participantId);

                return new TagReadResponse
                {
                    Success = true,
                    ReadRawId = readRaw.Id,
                    MatchedChipId = chip?.Id,
                    MatchedParticipantId = participantId,
                    MatchedBibNumber = bibNumber,
                    IsDuplicate = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tag read for EPC {Epc}", request.Epc);
                return new TagReadResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Process a batch of tag reads
        /// </summary>
        public async Task<TagReadBatchResponse> ProcessTagReadBatchAsync(TagReadBatchRequest request)
        {
            var response = new TagReadBatchResponse
            {
                TotalReceived = request.Reads.Count
            };

            var readerDeviceRepo = _repository.GetRepository<ReaderDevice>();
            var readRawRepo = _repository.GetRepository<ReadRaw>();
            var readerAssignmentRepo = _repository.GetRepository<ReaderAssignment>();

            // Get reader ID once for all reads
            var readerId = await GetReaderIdBySerialAsync(request.ReaderSerial);
            int? checkpointId = null;
            int eventId = 0;

            if (readerId.HasValue)
            {
                var reader = await readerDeviceRepo.GetQuery(r => r.Id == readerId.Value)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
                
                checkpointId = reader?.CheckpointId;
                
                var activeAssignment = await readerAssignmentRepo.GetQuery(
                        ra => ra.ReaderDeviceId == readerId.Value && !ra.AuditProperties.IsDeleted && ra.IsActive)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
                eventId = activeAssignment?.EventId ?? 0;
            }

            var readRawsToAdd = new List<ReadRaw>();

            // Process each read
            foreach (var read in request.Reads)
            {
                var result = new TagReadResultItem
                {
                    Epc = read.Epc,
                    Timestamp = read.Timestamp
                };

                try
                {
                    // Check duplicate
                    if (await IsDuplicateReadAsync(read.Epc, read.Timestamp, checkpointId))
                    {
                        result.Success = true;
                        result.IsDuplicate = true;
                        response.TotalDuplicates++;
                    }
                    else
                    {
                        // Find chip
                        var chip = await GetChipByEpcAsync(read.Epc);

                        // Get EventId from chip if not found from reader
                        var readEventId = eventId;
                        if (readEventId == 0 && chip != null)
                        {
                            var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
                            var assignment = await chipAssignmentRepo.GetQuery(
                                    ca => ca.ChipId == chip.Id && !ca.AuditProperties.IsDeleted)
                                .AsNoTracking()
                                .FirstOrDefaultAsync();
                            readEventId = assignment?.EventId ?? 0;
                        }

                        if (readEventId == 0)
                        {
                            result.Success = false;
                            result.Error = "Could not determine event";
                            response.TotalErrors++;
                        }
                        else
                        {
                            // Create ReadRaw using AutoMapper
                            var readRaw = _mapper.Map<ReadRaw>(read);
                            readRaw.EventId = readEventId;
                            readRaw.ReaderDeviceId = readerId ?? 0;
                            readRaw.CheckpointId = checkpointId;
                            readRaw.Source = "realtime";
                            readRaw.IsProcessed = false;
                            readRaw.AuditProperties = new AuditProperties
                            {
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            };

                            readRawsToAdd.Add(readRaw);
                            result.Success = true;
                            response.TotalProcessed++;

                            if (chip != null)
                                response.TotalMatched++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Error = ex.Message;
                    response.TotalErrors++;
                    _logger.LogWarning(ex, "Error processing batch read for EPC {Epc}", read.Epc);
                }

                response.Results.Add(result);
            }

            // Save all at once
            if (readRawsToAdd.Count > 0)
            {
                await readRawRepo.AddRangeAsync(readRawsToAdd);
                await _repository.SaveChangesAsync();
            }

            // Update reader status
            if (readerId.HasValue)
            {
                await UpdateReaderLastReadAsync(readerId.Value, response.TotalProcessed);
            }

            response.Success = response.TotalErrors == 0;

            _logger.LogInformation("Processed batch: {Received} received, {Processed} processed, {Duplicates} duplicates, {Errors} errors",
                response.TotalReceived, response.TotalProcessed, response.TotalDuplicates, response.TotalErrors);

            return response;
        }

        /// <summary>
        /// Process heartbeat from reader
        /// </summary>
        public async Task<HeartbeatResponse> ProcessHeartbeatAsync(ReaderHeartbeatRequest request)
        {
            var readerDeviceRepo = _repository.GetRepository<ReaderDevice>();
            var healthStatusRepo = _repository.GetRepository<ReaderHealthStatus>();
            var connectionLogRepo = _repository.GetRepository<ReaderConnectionLog>();
            var readerAssignmentRepo = _repository.GetRepository<ReaderAssignment>();

            var reader = await readerDeviceRepo.GetQuery(r => r.SerialNumber == request.ReaderSerial)
                .Include(r => r.Checkpoint)
                .FirstOrDefaultAsync();

            if (reader == null)
            {
                // Auto-register unknown reader
                var registration = await RegisterReaderAsync(new ReaderRegistrationRequest
                {
                    SerialNumber = request.ReaderSerial,
                    Hostname = request.ReaderHostname,
                    IpAddress = request.IpAddress,
                    FirmwareVersion = request.FirmwareVersion
                });

                reader = await readerDeviceRepo.GetByIdAsync(registration.ReaderId);
            }

            if (reader == null)
            {
                return new HeartbeatResponse
                {
                    Success = false
                };
            }

            // Update or create health status
            var health = await healthStatusRepo.GetQuery(h => h.ReaderDeviceId == reader.Id)
                .FirstOrDefaultAsync();

            if (health == null)
            {
                health = new ReaderHealthStatus
                {
                    ReaderDeviceId = reader.Id,
                    AuditProperties = new AuditProperties
                    {
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    }
                };
                await healthStatusRepo.AddAsync(health);
            }

            // Update health status using AutoMapper for partial update
            _mapper.Map(request, health);
            health.IsOnline = true;
            health.LastHeartbeat = DateTime.UtcNow;
            health.ReaderMode = ReaderMode.Active;

            await healthStatusRepo.UpdateAsync(health);

            // Update reader IP if changed
            if (!string.IsNullOrEmpty(request.IpAddress) && reader.IpAddress != request.IpAddress)
            {
                reader.IpAddress = request.IpAddress;
            }

            if (!string.IsNullOrEmpty(request.ReaderHostname) && reader.Hostname != request.ReaderHostname)
            {
                reader.Hostname = request.ReaderHostname;
            }

            await readerDeviceRepo.UpdateAsync(reader);

            // Log connection event
            var connectionLog = new ReaderConnectionLog
            {
                ReaderDeviceId = reader.Id,
                EventType = ReaderConnectionEventType.HeartbeatReceived,
                ConnectionProtocol = ConnectionProtocol.REST,
                IpAddress = request.IpAddress,
                Timestamp = DateTime.UtcNow,
                AuditProperties = new AuditProperties
                {
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true,
                    IsDeleted = false
                }
            };
            await connectionLogRepo.AddAsync(connectionLog);

            await _repository.SaveChangesAsync();

            // Get race info from assignment
            var activeAssignment = await readerAssignmentRepo.GetQuery(
                    ra => ra.ReaderDeviceId == reader.Id && !ra.AuditProperties.IsDeleted && ra.IsActive)
                .Include(ra => ra.Event)
                    .ThenInclude(e => e.Races)
                .AsNoTracking()
                .FirstOrDefaultAsync();
            
            var race = activeAssignment?.Event?.Races?.FirstOrDefault();

            return new HeartbeatResponse
            {
                Success = true,
                ReaderId = reader.Id,
                ReaderName = reader.SerialNumber,
                AssignedCheckpointId = reader.CheckpointId,
                AssignedCheckpointName = reader.Checkpoint?.Name,
                AssignedRaceId = race?.Id,
                ServerTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Register a new reader or update existing
        /// </summary>
        public async Task<ReaderRegistrationResponse> RegisterReaderAsync(ReaderRegistrationRequest request)
        {
            var readerDeviceRepo = _repository.GetRepository<ReaderDevice>();
            var healthStatusRepo = _repository.GetRepository<ReaderHealthStatus>();
            var antennaRepo = _repository.GetRepository<ReaderAntenna>();
            var connectionLogRepo = _repository.GetRepository<ReaderConnectionLog>();
            var checkpointRepo = _repository.GetRepository<Checkpoint>();
            var profileRepo = _repository.GetRepository<ReaderProfile>();
            var readerAssignmentRepo = _repository.GetRepository<ReaderAssignment>();

            var reader = await readerDeviceRepo.GetQuery(r => r.SerialNumber == request.SerialNumber)
                .FirstOrDefaultAsync();

            bool isNew = reader == null;

            if (isNew)
            {
                // Map request to entity using AutoMapper
                reader = _mapper.Map<ReaderDevice>(request);
                reader.TenantId = 1; // Default tenant, should be from context
                reader.ConnectionType = ConnectionType.Ethernet;
                reader.Status = "Online";
                reader.AuditProperties = new AuditProperties
                {
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true,
                    IsDeleted = false
                };

                await readerDeviceRepo.AddAsync(reader);
                await _repository.SaveChangesAsync();

                // Create health status
                var health = new ReaderHealthStatus
                {
                    ReaderDeviceId = reader.Id,
                    IsOnline = true,
                    LastHeartbeat = DateTime.UtcNow,
                    FirmwareVersion = request.FirmwareVersion,
                    ReaderMode = ReaderMode.Idle,
                    AuditProperties = new AuditProperties
                    {
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    }
                };
                await healthStatusRepo.AddAsync(health);

                // Create default antennas (R700 has 4 ports)
                for (byte port = 1; port <= 4; port++)
                {
                    var antenna = new ReaderAntenna
                    {
                        ReaderDeviceId = reader.Id,
                        AntennaPort = port,
                        AntennaName = $"Antenna {port}",
                        IsEnabled = true,
                        TxPowerCdBm = 3000, // 30 dBm default
                        AuditProperties = new AuditProperties
                        {
                            CreatedDate = DateTime.UtcNow,
                            IsActive = true,
                            IsDeleted = false
                        }
                    };
                    await antennaRepo.AddAsync(antenna);
                }

                // Log connection
                var connectionLog = new ReaderConnectionLog
                {
                    ReaderDeviceId = reader.Id,
                    EventType = ReaderConnectionEventType.Connected,
                    ConnectionProtocol = ConnectionProtocol.REST,
                    IpAddress = request.IpAddress,
                    Timestamp = DateTime.UtcNow,
                    AuditProperties = new AuditProperties
                    {
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    }
                };
                await connectionLogRepo.AddAsync(connectionLog);

                await _repository.SaveChangesAsync();

                _logger.LogInformation("Registered new reader: {Serial}", request.SerialNumber);
            }
            else
            {
                // Update existing reader using AutoMapper
                _mapper.Map(request, reader);

                await readerDeviceRepo.UpdateAsync(reader);
                await _repository.SaveChangesAsync();
            }

            // Clear cache
            _readerSerialCache.Clear();

            // Get config to return
            Checkpoint? checkpoint = null;
            if (reader.CheckpointId.HasValue)
            {
                checkpoint = await checkpointRepo.GetByIdAsync(reader.CheckpointId.Value);
            }

            // Get race from assignment
            var activeAssignment = await readerAssignmentRepo.GetQuery(
                    ra => ra.ReaderDeviceId == reader.Id && !ra.AuditProperties.IsDeleted && ra.IsActive)
                .Include(ra => ra.Event)
                    .ThenInclude(e => e.Races)
                .AsNoTracking()
                .FirstOrDefaultAsync();
            var race = activeAssignment?.Event?.Races?.FirstOrDefault();

            ReaderProfile? profile = null;
            if (reader.ProfileId.HasValue)
            {
                profile = await profileRepo.GetByIdAsync(reader.ProfileId.Value);
            }
            else
            {
                profile = await profileRepo.GetQuery(p => p.IsDefault).FirstOrDefaultAsync();
            }

            return new ReaderRegistrationResponse
            {
                Success = true,
                ReaderId = reader.Id,
                Message = isNew ? "Reader registered successfully" : "Reader updated successfully",
                Config = new ReaderConfigResponse
                {
                    ProfileId = profile?.Id,
                    ProfileName = profile?.ProfileName,
                    CheckpointId = checkpoint?.Id,
                    CheckpointName = checkpoint?.Name,
                    RaceId = race?.Id,
                    RaceName = race?.Title,
                    HeartbeatIntervalSeconds = 30,
                    DuplicateFilterMs = profile?.FilterDuplicateReadsMs ?? 1000
                }
            };
        }

        /// <summary>
        /// Get reader by serial number
        /// </summary>
        public async Task<ReaderDevice?> GetReaderBySerialAsync(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
                return null;

            var readerDeviceRepo = _repository.GetRepository<ReaderDevice>();
            return await readerDeviceRepo.GetQuery(r => r.SerialNumber == serialNumber)
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Check if read is duplicate
        /// </summary>
        public async Task<bool> IsDuplicateReadAsync(string epc, DateTime timestamp, int? checkpointId, int windowMs = 1000)
        {
            var windowStart = timestamp.AddMilliseconds(-windowMs);
            var windowEnd = timestamp.AddMilliseconds(windowMs);

            var readRawRepo = _repository.GetRepository<ReadRaw>();
            return await readRawRepo.GetQuery(r =>
                    (r.Epc == epc.ToUpperInvariant() || r.ChipEPC == epc.ToUpperInvariant()) &&
                    r.ReadTimestamp >= windowStart &&
                    r.ReadTimestamp <= windowEnd &&
                    (checkpointId == null || r.CheckpointId == checkpointId))
                .AsNoTracking()
                .AnyAsync();
        }

        #region Private Helpers

        private async Task<int?> GetReaderIdBySerialAsync(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
                return null;

            // Check cache
            RefreshCacheIfNeeded();
            if (_readerSerialCache.TryGetValue(serialNumber, out var cachedId))
                return cachedId;

            // Query database
            var readerDeviceRepo = _repository.GetRepository<ReaderDevice>();
            var reader = await readerDeviceRepo.GetQuery(r => r.SerialNumber == serialNumber)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (reader != null)
            {
                _readerSerialCache[serialNumber] = reader.Id;
                return reader.Id;
            }

            return null;
        }

        private async Task<Chip?> GetChipByEpcAsync(string epc)
        {
            if (string.IsNullOrEmpty(epc))
                return null;

            var upperEpc = epc.ToUpperInvariant();

            // Check cache
            RefreshCacheIfNeeded();
            if (_epcToChipCache.TryGetValue(upperEpc, out var cachedChipId))
            {
                var chipRepo = _repository.GetRepository<Chip>();
                return await chipRepo.GetByIdAsync(cachedChipId);
            }

            // Query database
            var chipRepository = _repository.GetRepository<Chip>();
            var chip = await chipRepository.GetQuery(c => c.EPC.ToUpper() == upperEpc && !c.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (chip != null)
            {
                _epcToChipCache[upperEpc] = chip.Id;
            }

            return chip;
        }

        private async Task UpdateReaderLastReadAsync(int readerId, int readCount = 1)
        {
            var healthStatusRepo = _repository.GetRepository<ReaderHealthStatus>();
            var health = await healthStatusRepo.GetQuery(h => h.ReaderDeviceId == readerId)
                .FirstOrDefaultAsync();

            if (health != null)
            {
                health.IsOnline = true;
                health.LastHeartbeat = DateTime.UtcNow;
                health.LastReadTimestamp = DateTime.UtcNow;
                health.TotalReadsToday += readCount;
                await healthStatusRepo.UpdateAsync(health);
                await _repository.SaveChangesAsync();
            }
        }

        private void RefreshCacheIfNeeded()
        {
            if (DateTime.UtcNow - _cacheLastRefresh > CacheExpiry)
            {
                _readerSerialCache.Clear();
                _epcToChipCache.Clear();
                _cacheLastRefresh = DateTime.UtcNow;
            }
        }

        #endregion
    }
}
