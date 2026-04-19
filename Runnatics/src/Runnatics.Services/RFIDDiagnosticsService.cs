using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Responses.RFID;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class RFIDDiagnosticsService : ServiceBase<IUnitOfWork<RaceSyncDbContext>>, IRFIDDiagnosticsService
    {
        private readonly ILogger<RFIDDiagnosticsService> _logger;
        private readonly IEncryptionService _encryptionService;

        public RFIDDiagnosticsService(
            IUnitOfWork<RaceSyncDbContext> repository,
            ILogger<RFIDDiagnosticsService> logger,
            IEncryptionService encryptionService) : base(repository)
        {
            _logger = logger;
            _encryptionService = encryptionService;
        }

        public async Task<RFIDDiagnosticsResponse> DiagnoseProcessingAsync(string eventId, string raceId)
        {
            var response = new RFIDDiagnosticsResponse();

            int decryptedEventId;
            int decryptedRaceId;
            try
            {
                decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to decrypt event/race IDs: {ex.Message}";
                response.Status = "Failed";
                return response;
            }

            response.RaceEventSetup.EventId = decryptedEventId;
            response.RaceEventSetup.RaceId = decryptedRaceId;

            // ── SECTION A: Race & Event Setup ──
            var eventRow = await _repository.GetRepository<Event>()
                .GetQuery(e => e.Id == decryptedEventId, ignoreQueryFilters: true)
                .AsNoTracking()
                .Select(e => new { e.Id, e.Name, e.EventDate, e.TimeZone })
                .FirstOrDefaultAsync();

            var raceRow = await _repository.GetRepository<Race>()
                .GetQuery(r => r.Id == decryptedRaceId && r.EventId == decryptedEventId, ignoreQueryFilters: true)
                .AsNoTracking()
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.Distance,
                    r.StartTime,
                    r.AuditProperties.IsActive,
                    r.AuditProperties.IsDeleted
                })
                .FirstOrDefaultAsync();

            var raceSettingsRow = await _repository.GetRepository<RaceSettings>()
                .GetQuery(rs => rs.RaceId == decryptedRaceId, ignoreQueryFilters: true)
                .AsNoTracking()
                .Select(rs => new { rs.HasLoops, rs.LoopLength })
                .FirstOrDefaultAsync();

            response.RaceEventSetup.EventName = eventRow?.Name;
            response.RaceEventSetup.EventDate = eventRow?.EventDate;
            response.RaceEventSetup.TimeZone = eventRow?.TimeZone;
            response.RaceEventSetup.RaceExists = raceRow != null;
            response.RaceEventSetup.RaceName = raceRow?.Title;
            response.RaceEventSetup.RaceDistance = raceRow?.Distance;
            response.RaceEventSetup.RaceStartTime = raceRow?.StartTime;
            response.RaceEventSetup.RaceIsActive = raceRow?.IsActive;
            response.RaceEventSetup.RaceIsDeleted = raceRow?.IsDeleted;
            response.RaceEventSetup.HasLoops = raceSettingsRow?.HasLoops;
            response.RaceEventSetup.LoopLength = raceSettingsRow?.LoopLength;

            // ── SECTION B: Checkpoints ──
            var checkpointRepo = _repository.GetRepository<Checkpoint>();
            var checkpoints = await checkpointRepo
                .GetQuery(cp => cp.RaceId == decryptedRaceId && cp.EventId == decryptedEventId,
                    ignoreQueryFilters: true)
                .AsNoTracking()
                .Select(cp => new
                {
                    cp.Id,
                    cp.Name,
                    cp.DistanceFromStart,
                    cp.DeviceId,
                    cp.ParentDeviceId,
                    cp.IsMandatory,
                    cp.AuditProperties.IsActive,
                    cp.AuditProperties.IsDeleted
                })
                .ToListAsync();

            var checkpointDeviceIds = checkpoints.Select(c => c.DeviceId).Distinct().ToList();
            var devicesForCheckpoints = await _repository.GetRepository<Device>()
                .GetQuery(d => checkpointDeviceIds.Contains(d.Id), ignoreQueryFilters: true)
                .AsNoTracking()
                .Select(d => new { d.Id, d.Name, d.DeviceMacAddress })
                .ToListAsync();
            var deviceById = devicesForCheckpoints.ToDictionary(d => d.Id, d => d);

            response.Checkpoints.TotalCount = checkpoints.Count;
            foreach (var cp in checkpoints)
            {
                deviceById.TryGetValue(cp.DeviceId, out var dev);
                response.Checkpoints.Items.Add(new CheckpointDiagnosticInfo
                {
                    Id = cp.Id,
                    Name = cp.Name,
                    DistanceFromStart = cp.DistanceFromStart,
                    DeviceId = cp.DeviceId,
                    ParentDeviceId = cp.ParentDeviceId,
                    IsMandatory = cp.IsMandatory,
                    IsActive = cp.IsActive,
                    IsDeleted = cp.IsDeleted,
                    DeviceName = dev?.Name,
                    DeviceMacAddress = dev?.DeviceMacAddress,
                    DeviceFound = dev != null
                });
                if (dev == null)
                {
                    response.Checkpoints.Flags.Add(
                        $"Checkpoint {cp.Id} ('{cp.Name}') references DeviceId {cp.DeviceId} which does not exist in Devices table");
                }
            }

            var checkpointDeviceMacs = devicesForCheckpoints
                .Where(d => !string.IsNullOrWhiteSpace(d.DeviceMacAddress))
                .Select(d => d.DeviceMacAddress!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // ── SECTION C: Participants ──
            var participantRepo = _repository.GetRepository<Participant>();
            var totalActiveParticipants = await participantRepo
                .GetQuery(p => p.RaceId == decryptedRaceId &&
                               p.EventId == decryptedEventId &&
                               p.AuditProperties.IsActive &&
                               !p.AuditProperties.IsDeleted)
                .AsNoTracking()
                .CountAsync();

            var activeParticipantIds = await participantRepo
                .GetQuery(p => p.RaceId == decryptedRaceId &&
                               p.EventId == decryptedEventId &&
                               p.AuditProperties.IsActive &&
                               !p.AuditProperties.IsDeleted)
                .AsNoTracking()
                .Select(p => p.Id)
                .ToListAsync();

            var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
            var participantsWithChip = await chipAssignmentRepo
                .GetQuery(ca => ca.EventId == decryptedEventId &&
                                activeParticipantIds.Contains(ca.ParticipantId) &&
                                ca.UnassignedAt == null)
                .AsNoTracking()
                .Select(ca => ca.ParticipantId)
                .Distinct()
                .CountAsync();

            response.Participants.TotalActive = totalActiveParticipants;
            response.Participants.WithChipAssignment = participantsWithChip;
            response.Participants.WithoutChipAssignment = totalActiveParticipants - participantsWithChip;

            // ── SECTION D: Upload Batches ──
            var batchRepo = _repository.GetRepository<UploadBatch>();
            var batches = await batchRepo
                .GetQuery(b => b.EventId == decryptedEventId &&
                               (b.RaceId == decryptedRaceId || b.RaceId == null),
                          ignoreQueryFilters: true)
                .AsNoTracking()
                .Select(b => new
                {
                    b.Id,
                    b.Status,
                    b.SourceType,
                    b.AuditProperties.CreatedDate,
                    b.OriginalFileName,
                    b.StoredFilePath,
                    b.TotalReadings,
                    b.DeviceId
                })
                .ToListAsync();

            response.UploadBatches.TotalCount = batches.Count;
            foreach (var b in batches)
            {
                response.UploadBatches.Items.Add(new UploadBatchDiagnosticInfo
                {
                    BatchId = b.Id,
                    Status = b.Status,
                    SourceType = b.SourceType,
                    CreatedAt = b.CreatedDate,
                    OriginalFileName = b.OriginalFileName,
                    StoredFilePath = b.StoredFilePath,
                    TotalReadings = b.TotalReadings,
                    DeviceMacAddress = b.DeviceId
                });
            }

            var batchIds = batches.Select(b => b.Id).ToList();

            // ── SECTION E: Raw RFID Readings ──
            var readingRepo = _repository.GetRepository<RawRFIDReading>();
            var rawReadingsQuery = readingRepo
                .GetQuery(r => batchIds.Contains(r.BatchId), ignoreQueryFilters: true)
                .AsNoTracking();

            response.RawReadings.TotalCount = await rawReadingsQuery.CountAsync();

            var byProcessResult = await rawReadingsQuery
                .GroupBy(r => r.ProcessResult)
                .Select(g => new { ProcessResult = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var pr in byProcessResult)
            {
                response.RawReadings.ByProcessResult[pr.ProcessResult ?? "(null)"] = pr.Count;
            }

            var byDeviceMac = await rawReadingsQuery
                .GroupBy(r => r.DeviceId)
                .Select(g => new { DeviceMac = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var dm in byDeviceMac)
            {
                response.RawReadings.ByDeviceMac[dm.DeviceMac ?? "(null)"] = dm.Count;
            }

            var samples = await rawReadingsQuery
                .OrderBy(r => r.Id)
                .Take(5)
                .Select(r => new RawReadingSample
                {
                    Id = r.Id,
                    Epc = r.Epc,
                    DeviceId = r.DeviceId,
                    ReadTimeUtc = r.ReadTimeUtc,
                    RssiDbm = r.RssiDbm,
                    ProcessResult = r.ProcessResult
                })
                .ToListAsync();
            response.RawReadings.Samples = samples;

            // Flag: MACs in readings that don't exist in Devices table
            var readingMacs = response.RawReadings.ByDeviceMac.Keys
                .Where(m => !string.IsNullOrWhiteSpace(m) && m != "(null)")
                .Select(m => m.Trim())
                .ToList();

            if (readingMacs.Count > 0)
            {
                var allDeviceMacs = await _repository.GetRepository<Device>()
                    .GetQuery(d => d.DeviceMacAddress != null, ignoreQueryFilters: true)
                    .AsNoTracking()
                    .Select(d => d.DeviceMacAddress!)
                    .ToListAsync();
                var allDeviceMacSet = allDeviceMacs.ToHashSet(StringComparer.OrdinalIgnoreCase);

                var unknownMacs = readingMacs
                    .Where(m => !allDeviceMacSet.Contains(m))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                response.RawReadings.UnknownMacsInDevicesTable = unknownMacs;
                if (unknownMacs.Count > 0)
                {
                    response.RawReadings.Flags.Add(
                        $"Reading MACs not found in Devices table: {string.Join(", ", unknownMacs)}");
                }

                var noCheckpointMacs = readingMacs
                    .Where(m => !checkpointDeviceMacs.Contains(m))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                response.RawReadings.MacsWithoutCheckpoint = noCheckpointMacs;
                if (noCheckpointMacs.Count > 0)
                {
                    response.RawReadings.Flags.Add(
                        $"Reading MACs with no checkpoint configured for this race: {string.Join(", ", noCheckpointMacs)}");
                }
            }

            // ── SECTION F: Checkpoint Assignments ──
            var checkpointIds = checkpoints.Select(c => c.Id).ToList();
            var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();
            var assignmentsGrouped = await assignmentRepo
                .GetQuery(a => checkpointIds.Contains(a.CheckpointId), ignoreQueryFilters: true)
                .AsNoTracking()
                .GroupBy(a => a.CheckpointId)
                .Select(g => new { CheckpointId = g.Key, Count = g.Count() })
                .ToListAsync();

            response.Assignments.TotalCount = assignmentsGrouped.Sum(x => x.Count);
            foreach (var ag in assignmentsGrouped)
            {
                response.Assignments.ByCheckpointId[ag.CheckpointId] = ag.Count;
            }

            // ── SECTION G: Normalized Readings ──
            var normalizedRepo = _repository.GetRepository<ReadNormalized>();
            var normalizedQuery = normalizedRepo
                .GetQuery(n => n.EventId == decryptedEventId &&
                               checkpointIds.Contains(n.CheckpointId),
                          ignoreQueryFilters: true)
                .AsNoTracking();

            response.NormalizedReadings.TotalCount = await normalizedQuery.CountAsync();
            response.NormalizedReadings.DistinctParticipants = await normalizedQuery
                .Select(n => n.ParticipantId)
                .Distinct()
                .CountAsync();

            // ── SECTION H: Split Times ──
            var splitRepo = _repository.GetRepository<SplitTimes>();
            var splitGrouped = await splitRepo
                .GetQuery(s => s.EventId == decryptedEventId &&
                               checkpointIds.Contains(s.ToCheckpointId),
                          ignoreQueryFilters: true)
                .AsNoTracking()
                .GroupBy(s => s.ToCheckpointId)
                .Select(g => new { CheckpointId = g.Key, Count = g.Count() })
                .ToListAsync();

            response.SplitTimes.TotalCount = splitGrouped.Sum(x => x.Count);
            foreach (var sg in splitGrouped)
            {
                response.SplitTimes.ByCheckpointId[sg.CheckpointId] = sg.Count;
            }

            // ── SECTION I: Results ──
            var resultRepo = _repository.GetRepository<Results>();
            var resultsGrouped = await resultRepo
                .GetQuery(r => r.RaceId == decryptedRaceId && r.EventId == decryptedEventId,
                          ignoreQueryFilters: true)
                .AsNoTracking()
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            response.Results.TotalCount = resultsGrouped.Sum(x => x.Count);
            foreach (var rg in resultsGrouped)
            {
                response.Results.ByStatus[rg.Status ?? "(null)"] = rg.Count;
            }

            // ── SECTION J: EPC to BIB Mapping ──
            response.EpcBibMapping.ParticipantsWithChip = participantsWithChip;

            var chipSamples = await chipAssignmentRepo
                .GetQuery(ca => ca.EventId == decryptedEventId &&
                                activeParticipantIds.Contains(ca.ParticipantId) &&
                                ca.UnassignedAt == null,
                          ignoreQueryFilters: true)
                .AsNoTracking()
                .Take(5)
                .Select(ca => new
                {
                    ca.ParticipantId,
                    BibNumber = ca.Participant.BibNumber,
                    Epc = ca.Chip.EPC
                })
                .ToListAsync();

            foreach (var s in chipSamples)
            {
                response.EpcBibMapping.Samples.Add(new ParticipantChipSample
                {
                    ParticipantId = s.ParticipantId,
                    BibNumber = s.BibNumber,
                    Epc = s.Epc
                });
            }

            var sampleEpcs = samples.Select(s => s.Epc).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().Take(5).ToList();
            if (sampleEpcs.Count > 0)
            {
                var lookups = await chipAssignmentRepo
                    .GetQuery(ca => ca.EventId == decryptedEventId &&
                                    sampleEpcs.Contains(ca.Chip.EPC) &&
                                    ca.UnassignedAt == null,
                              ignoreQueryFilters: true)
                    .AsNoTracking()
                    .Select(ca => new
                    {
                        Epc = ca.Chip.EPC,
                        ca.ParticipantId,
                        BibNumber = ca.Participant.BibNumber
                    })
                    .ToListAsync();

                foreach (var epc in sampleEpcs)
                {
                    var match = lookups.FirstOrDefault(l => string.Equals(l.Epc, epc, StringComparison.OrdinalIgnoreCase));
                    response.EpcBibMapping.RawEpcLookups.Add(new EpcLookupResult
                    {
                        Epc = epc,
                        MatchesAnyParticipant = match != null,
                        ParticipantId = match?.ParticipantId,
                        BibNumber = match?.BibNumber
                    });
                }
            }

            // ── SECTION K: Diagnosis Summary ──
            var issues = response.DiagnosisSummary.LikelyIssues;

            if (!response.RaceEventSetup.RaceExists)
            {
                issues.Add("Race not found for the given Event/Race IDs");
            }

            if (response.Checkpoints.TotalCount == 0)
            {
                issues.Add("No checkpoints configured for this race");
            }

            if (response.RawReadings.TotalCount == 0)
            {
                issues.Add("No RFID readings uploaded for this race");
            }
            else
            {
                var pending = response.RawReadings.ByProcessResult.GetValueOrDefault("Pending", 0);
                var invalid = response.RawReadings.ByProcessResult.GetValueOrDefault("Invalid", 0);
                var success = response.RawReadings.ByProcessResult.GetValueOrDefault("Success", 0);
                var total = response.RawReadings.TotalCount;

                if (pending == total)
                {
                    issues.Add("Phase 1 validation didn't run — all readings are still 'Pending'");
                }
                if (invalid == total && total > 0)
                {
                    issues.Add("RSSI threshold rejected all readings — check signal quality");
                }

                if (response.RawReadings.UnknownMacsInDevicesTable.Count > 0)
                {
                    issues.Add($"Device MAC mismatch — MACs not in Devices table: {string.Join(", ", response.RawReadings.UnknownMacsInDevicesTable)}");
                }

                if (response.RawReadings.MacsWithoutCheckpoint.Count > 0)
                {
                    issues.Add($"Checkpoint-Device wiring mismatch — these MACs have readings but no checkpoint is configured for them: {string.Join(", ", response.RawReadings.MacsWithoutCheckpoint)}");
                }

                if (response.NormalizedReadings.TotalCount == 0 && success > 0)
                {
                    issues.Add("Phase 1.5 checkpoint assignment failed — Success readings exist but no normalized rows produced");
                }
            }

            if (response.Participants.TotalActive > 0 && response.Participants.WithChipAssignment == 0)
            {
                issues.Add("No BIB-to-EPC mapping done — run BIB mapping first");
            }

            if (response.EpcBibMapping.RawEpcLookups.Count > 0 &&
                response.EpcBibMapping.RawEpcLookups.All(l => !l.MatchesAnyParticipant))
            {
                issues.Add("EPC-to-Bib mapping exists but reading EPCs don't match any mapped BIB");
            }

            if (response.SplitTimes.TotalCount == 0 && response.NormalizedReadings.TotalCount > 0)
            {
                issues.Add("Phase 2.5 split time creation failed — normalized readings exist but no splits produced");
            }

            if (response.Results.TotalCount == 0 && response.SplitTimes.TotalCount > 0)
            {
                issues.Add("Phase 3 result calculation failed — splits exist but no results produced");
            }

            _logger.LogInformation(
                "RFID diagnostics completed for Event {EventId} / Race {RaceId}. Issues found: {IssueCount}",
                decryptedEventId, decryptedRaceId, issues.Count);

            return response;
        }
    }
}
