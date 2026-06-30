using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Client.Responses.Results;
using Runnatics.Models.Client.Responses.RFID;
using Runnatics.Models.Data.Constants;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Helpers;
using Runnatics.Services.Interface;
using ResultsSplitTimeInfo = Runnatics.Models.Client.Responses.Results.SplitTimeInfo;

namespace Runnatics.Services
{
    public class ResultsService : ServiceBase<IUnitOfWork<RaceSyncDbContext>>, IResultsService
    {
        private readonly IMapper _mapper;
        private readonly ILogger<ResultsService> _logger;
        private readonly IUserContextService _userContext;
        private readonly IEncryptionService _encryptionService;

        private readonly IRFIDImportService _rfidImportService;
        // Resolve IRaceNotificationService from a fresh scope per call (see RecordManualTimeAsync) —
        // it shares the request DbContext, so it must never run on a background thread against it.
        private readonly IServiceScopeFactory _scopeFactory;

        public ResultsService(
            IUnitOfWork<RaceSyncDbContext> repository,
            IMapper mapper,
            ILogger<ResultsService> logger,
            IUserContextService userContext,
            IEncryptionService encryptionService,
            IRFIDImportService rfidImportService,
            IServiceScopeFactory scopeFactory)
            : base(repository)
        {
            _mapper = mapper;
            _logger = logger;
            _userContext = userContext;
            _encryptionService = encryptionService;
            _rfidImportService = rfidImportService;
            _scopeFactory = scopeFactory;
        }

        public async Task<SplitTimeCalculationResponse> CalculateSplitTimesAsync(CalculateSplitTimesRequest request)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(request.EventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(request.RaceId));
            var startTime = DateTime.UtcNow;

            var response = new SplitTimeCalculationResponse
            {
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting split time calculation for Race {RaceId}", decryptedRaceId);

                // Get race information
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r =>
                    r.Id == decryptedRaceId &&
                    r.EventId == decryptedEventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (race == null)
                {
                    ErrorMessage = "Race not found";
                    response.Status = "Failed";
                    return response;
                }

                // Get checkpoints ordered by distance
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var checkpoints = await checkpointRepo.GetQuery(c =>
                    c.RaceId == decryptedRaceId &&
                    c.EventId == decryptedEventId &&
                    c.AuditProperties.IsActive &&
                    !c.AuditProperties.IsDeleted)
                    .OrderBy(c => c.DistanceFromStart)
                    .ToListAsync();

                if (checkpoints.Count == 0)
                {
                    ErrorMessage = "No checkpoints found for this race";
                    response.Status = "Failed";
                    return response;
                }

                // Get normalized readings grouped by participant
                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var splitTimeRepo = _repository.GetRepository<SplitTimes>();

                // Delete existing split times if force recalculation
                if (request.ForceRecalculation)
                {
                    var existingSplits = await splitTimeRepo.GetQuery(st =>
                        st.EventId == decryptedEventId &&
                        st.Participant.RaceId == decryptedRaceId)
                        .ToListAsync();

                    if (existingSplits.Any())
                    {
                        foreach (var split in existingSplits)
                        {
                            split.AuditProperties.IsDeleted = true;
                            split.AuditProperties.IsActive = false;
                            split.AuditProperties.UpdatedBy = userId;
                            split.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        }
                        await splitTimeRepo.UpdateRangeAsync(existingSplits);
                        await _repository.SaveChangesAsync();
                    }
                }

                // Get participants with readings
                var participantReadings = await normalizedRepo.GetQuery(rn =>
                    rn.EventId == decryptedEventId &&
                    rn.Participant.RaceId == decryptedRaceId &&
                    rn.AuditProperties.IsActive &&
                    !rn.AuditProperties.IsDeleted)
                    .Include(rn => rn.Participant)
                    .Include(rn => rn.Checkpoint)
                    .GroupBy(rn => rn.ParticipantId)
                    .Select(g => new
                    {
                        ParticipantId = g.Key,
                        Readings = g.OrderBy(r => r.Checkpoint.DistanceFromStart).ToList()
                    })
                    .ToListAsync();

                response.TotalParticipants = participantReadings.Count;

                var splitTimes = new List<SplitTimes>();
                var checkpointSummaries = new Dictionary<int, CheckpointSummary>();

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    foreach (var participantData in participantReadings)
                    {
                        long? previousSplitTime = null;
                        int? previousCheckpointId = null;
                        var participantHasSplits = false;

                        foreach (var reading in participantData.Readings)
                        {
                            var checkpoint = checkpoints.FirstOrDefault(c => c.Id == reading.CheckpointId);
                            if (checkpoint == null) continue;

                            if (!reading.GunTime.HasValue || reading.GunTime.Value <= 0) continue;
                            var splitTimeMs = reading.GunTime.Value;
                            long? segmentTimeMs = null;

                            if (previousSplitTime.HasValue)
                            {
                                segmentTimeMs = splitTimeMs - previousSplitTime.Value;
                            }

                            // Calculate pace (min/km)
                            decimal? pace = null;
                            if (checkpoint.DistanceFromStart > 0)
                            {
                                var timeInMinutes = splitTimeMs / 60000.0m; // Convert ms to minutes
                                pace = timeInMinutes / checkpoint.DistanceFromStart;
                            }

                            var splitTime = new SplitTimes
                            {
                                EventId = decryptedEventId,
                                ParticipantId = participantData.ParticipantId,
                                ToCheckpointId = reading.CheckpointId,
                                CheckpointId = reading.CheckpointId,
                                FromCheckpointId = previousCheckpointId ?? reading.CheckpointId,
                                ReadNormalizedId = reading.Id,
                                SplitTimeMs = splitTimeMs,
                                SplitTime = TimeSpan.FromMilliseconds(splitTimeMs),
                                SegmentTime = segmentTimeMs,
                                Pace = pace,
                                AuditProperties = new Models.Data.Common.AuditProperties
                                {
                                    CreatedBy = userId,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                }
                            };

                            splitTimes.Add(splitTime);
                            participantHasSplits = true;
                            previousSplitTime = splitTimeMs;
                            previousCheckpointId = reading.CheckpointId;

                            // Update checkpoint summary
                            if (!checkpointSummaries.ContainsKey(reading.CheckpointId))
                            {
                                checkpointSummaries[reading.CheckpointId] = new CheckpointSummary
                                {
                                    CheckpointId = _encryptionService.Encrypt(reading.CheckpointId.ToString()),
                                    CheckpointName = checkpoint.Name ?? $"CP{checkpoint.DistanceFromStart}km",
                                    DistanceKm = checkpoint.DistanceFromStart,
                                    ParticipantCount = 0
                                };
                            }

                            var summary = checkpointSummaries[reading.CheckpointId];
                            summary.ParticipantCount++;

                            if (!summary.FastestTimeMs.HasValue || splitTimeMs < summary.FastestTimeMs.Value)
                            {
                                summary.FastestTimeMs = splitTimeMs;
                                summary.FastestTimeFormatted = FormatTime(splitTimeMs);
                            }

                            if (!summary.SlowestTimeMs.HasValue || splitTimeMs > summary.SlowestTimeMs.Value)
                            {
                                summary.SlowestTimeMs = splitTimeMs;
                                summary.SlowestTimeFormatted = FormatTime(splitTimeMs);
                            }
                        }

                        if (participantHasSplits)
                        {
                            response.ParticipantsWithSplits++;
                        }
                    }

                    // Bulk insert split times
                    if (splitTimes.Any())
                    {
                        await splitTimeRepo.AddRangeAsync(splitTimes);
                        await CalculateSplitTimeRankingsAsync(decryptedEventId, decryptedRaceId, userId);
                    }
                });

                response.TotalSplitTimesCreated = splitTimes.Count;
                response.CheckpointsProcessed = checkpointSummaries.Count;
                response.CheckpointSummaries = checkpointSummaries.Values.OrderBy(c => c.DistanceKm).ToList();
                response.Status = "Completed";

                var endTime = DateTime.UtcNow;
                response.ProcessingTimeMs = (long)(endTime - startTime).TotalMilliseconds;

                _logger.LogInformation(
                    "Split time calculation completed. Participants: {Participants}, Splits: {Splits}, Time: {Time}ms",
                    response.ParticipantsWithSplits, response.TotalSplitTimesCreated, response.ProcessingTimeMs);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error calculating split times: {ex.Message}";
                _logger.LogError(ex, "Error calculating split times");
                response.Status = "Failed";
                return response;
            }
        }

        public async Task<ResultsCalculationResponse> CalculateResultsAsync(CalculateResultsRequest request)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(request.EventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(request.RaceId));
            var startTime = DateTime.UtcNow;

            var response = new ResultsCalculationResponse
            {
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting results calculation for Race {RaceId}", decryptedRaceId);

                // Load all checkpoints for the race
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var allCheckpoints = await checkpointRepo.GetQuery(c =>
                    c.RaceId == decryptedRaceId &&
                    c.EventId == decryptedEventId &&
                    c.AuditProperties.IsActive &&
                    !c.AuditProperties.IsDeleted)
                    .OrderByDescending(c => c.DistanceFromStart)
                    .ToListAsync();

                if (allCheckpoints.Count == 0)
                {
                    ErrorMessage = "No checkpoints found for this race";
                    response.Status = "Failed";
                    return response;
                }

                // BUG-26: mandatory evaluation is per-DISTANCE, not per-checkpoint-ID. Two devices
                // at the same DistanceFromStart are ONE logical gate — a detection at ANY checkpoint
                // at that distance satisfies it. Fall back to the highest distance if none flagged.
                // Mirrors RFIDImportService.CalculateRaceResultsAsync.
                var mandatoryCheckpoints = allCheckpoints.Where(c => c.IsMandatory).ToList();
                var mandatoryDistances = mandatoryCheckpoints.Count > 0
                    ? mandatoryCheckpoints.Select(c => c.DistanceFromStart).Distinct().ToList()
                    : new List<decimal> { allCheckpoints.First().DistanceFromStart }; // already sorted descending

                var idsByMandatoryDistance = mandatoryDistances.ToDictionary(
                    d => d,
                    d => allCheckpoints.Where(c => c.DistanceFromStart == d).Select(c => c.Id).ToHashSet());

                // Finish gate = ALL checkpoints at the highest mandatory distance — finish time and
                // status must come from the same rule.
                var finishGateDistance = mandatoryDistances.Max();
                var finishGateCheckpointIds = idsByMandatoryDistance[finishGateDistance];

                // Get all participants in the race
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var allParticipants = await participantRepo.GetQuery(p =>
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .ToListAsync();

                response.TotalParticipants = allParticipants.Count;

                // Get all split times for the race grouped by participant (used for FinishTime only)
                var splitTimeRepo = _repository.GetRepository<SplitTimes>();
                var allRaceSplits = await splitTimeRepo.GetQuery(st =>
                    st.EventId == decryptedEventId &&
                    st.Participant.RaceId == decryptedRaceId &&
                    st.AuditProperties.IsActive &&
                    !st.AuditProperties.IsDeleted)
                    .ToListAsync();

                var splitsByParticipant = allRaceSplits
                    .GroupBy(st => st.ParticipantId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Load ReadNormalized detections at the mandatory gates for all race participants.
                // This is the source of truth for Finished vs DNF — EPC reads AND manual entries both land here.
                var participantIds = allParticipants.Select(p => p.Id).ToList();
                var mandatoryGateIdList = idsByMandatoryDistance.Values.SelectMany(ids => ids).Distinct().ToList();
                var readNormalizedRepo = _repository.GetRepository<ReadNormalized>();
                var allDetections = await readNormalizedRepo.GetQuery(rn =>
                    participantIds.Contains(rn.ParticipantId) &&
                    mandatoryGateIdList.Contains(rn.CheckpointId) &&
                    !rn.AuditProperties.IsDeleted)
                    .Select(rn => new { rn.ParticipantId, rn.CheckpointId })
                    .ToListAsync();

                var detectionsByParticipant = allDetections
                    .GroupBy(d => d.ParticipantId)
                    .ToDictionary(g => g.Key, g => g.Select(d => d.CheckpointId).ToHashSet());

                // Finisher = every mandatory DISTANCE satisfied by a detection at ANY checkpoint
                // at that distance
                var finisherIds = allParticipants
                    .Where(p =>
                    {
                        var covered = detectionsByParticipant.GetValueOrDefault(p.Id, []);
                        return mandatoryDistances.All(d => idsByMandatoryDistance[d].Overlaps(covered));
                    })
                    .Select(p => p.Id)
                    .ToHashSet();

                response.Finishers = finisherIds.Count;
                response.DNF = response.TotalParticipants - response.Finishers;

                // Delete existing results if force recalculation
                var resultsRepo = _repository.GetRepository<Results>();
                if (request.ForceRecalculation)
                {
                    var existingResults = await resultsRepo.GetQuery(r =>
                        r.EventId == decryptedEventId &&
                        r.RaceId == decryptedRaceId)
                        .ToListAsync();

                    if (existingResults.Any())
                    {
                        foreach (var result in existingResults)
                        {
                            result.AuditProperties.IsDeleted = true;
                            result.AuditProperties.IsActive = false;
                            result.AuditProperties.UpdatedBy = userId;
                            result.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        }
                        await resultsRepo.UpdateRangeAsync(existingResults);
                        await _repository.SaveChangesAsync();
                    }
                }

                var results = new List<Results>();

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    foreach (var participant in allParticipants)
                    {
                        var pSplits = splitsByParticipant.GetValueOrDefault(participant.Id, []);
                        var covered = detectionsByParticipant.GetValueOrDefault(participant.Id, []);
                        bool isFinisher = mandatoryDistances.All(d => idsByMandatoryDistance[d].Overlaps(covered));

                        string status = isFinisher ? ResultStatus.Finished : ResultStatus.DNF;
                        long? finishTime = null;

                        if (isFinisher)
                        {
                            // BUG-26: finish time from a split at ANY checkpoint at the finish-gate
                            // distance (earliest wins) — same rule that granted Finished status.
                            finishTime = pSplits
                                .Where(st => finishGateCheckpointIds.Contains(st.ToCheckpointId) ||
                                             (st.CheckpointId.HasValue && finishGateCheckpointIds.Contains(st.CheckpointId.Value)))
                                .OrderBy(st => st.SplitTimeMs)
                                .FirstOrDefault()?.SplitTimeMs;
                        }

                        results.Add(new Results
                        {
                            EventId = decryptedEventId,
                            ParticipantId = participant.Id,
                            RaceId = decryptedRaceId,
                            FinishTime = finishTime,
                            GunTime = finishTime,
                            NetTime = finishTime,
                            Status = status,
                            IsOfficial = request.MarkAsOfficial,
                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        });
                    }

                    if (results.Any())
                    {
                        await resultsRepo.AddRangeAsync(results);
                        await CalculateResultRankingsAsync(decryptedEventId, decryptedRaceId, userId);
                    }
                });

                // Get fastest and slowest times
                var finishedResults = results.Where(r => r.Status == "Finished" && r.FinishTime.HasValue).ToList();
                if (finishedResults.Any())
                {
                    response.FastestFinishTimeMs = finishedResults.Min(r => r.FinishTime);
                    response.SlowestFinishTimeMs = finishedResults.Max(r => r.FinishTime);

                    if (response.FastestFinishTimeMs.HasValue)
                        response.FastestFinishTimeFormatted = FormatTime(response.FastestFinishTimeMs.Value);

                    if (response.SlowestFinishTimeMs.HasValue)
                        response.SlowestFinishTimeFormatted = FormatTime(response.SlowestFinishTimeMs.Value);
                }

                response.Status = "Completed";

                var endTime = DateTime.UtcNow;
                response.ProcessingTimeMs = (long)(endTime - startTime).TotalMilliseconds;

                _logger.LogInformation(
                    "Results calculation completed. Finishers: {Finishers}, DNF: {DNF}, Time: {Time}ms",
                    response.Finishers, response.DNF, response.ProcessingTimeMs);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error calculating results: {ex.Message}";
                _logger.LogError(ex, "Error calculating results");
                response.Status = "Failed";
                return response;
            }
        }

        public async Task<LeaderboardResponse> GetLeaderboardAsync(GetLeaderboardRequest request)
        {
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(request.EventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(request.RaceId));

            try
            {
                // Load settings
                var eventSettingsRepo = _repository.GetRepository<EventSettings>();
                var eventSettings = await eventSettingsRepo.GetQuery(es =>
                    es.EventId == decryptedEventId &&
                    es.AuditProperties.IsActive &&
                    !es.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                var raceSettingsRepo = _repository.GetRepository<RaceSettings>();
                var raceSettings = await raceSettingsRepo.GetQuery(rs =>
                    rs.RaceId == decryptedRaceId &&
                    rs.AuditProperties.IsActive &&
                    !rs.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                var leaderboardSettingsRepo = _repository.GetRepository<LeaderboardSettings>();

                // Check race-level override first, fall back to event-level
                var leaderboardSettings = await leaderboardSettingsRepo.GetQuery(ls =>
                    ls.RaceId == decryptedRaceId &&
                    ls.OverrideSettings == true &&
                    ls.AuditProperties.IsActive &&
                    !ls.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                leaderboardSettings ??= await leaderboardSettingsRepo.GetQuery(ls =>
                    ls.EventId == decryptedEventId &&
                    ls.RaceId == null &&
                    ls.AuditProperties.IsActive &&
                    !ls.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                // Check if leaderboard is allowed — skipped for admin export callers
                if (!request.SkipPublishGates)
                {
                    if (raceSettings != null && !raceSettings.ShowLeaderboard)
                    {
                        ErrorMessage = "Leaderboard is not enabled for this race";
                        return new LeaderboardResponse();
                    }

                    if (eventSettings != null && !eventSettings.Published)
                    {
                        ErrorMessage = "Event results are not published yet";
                        return new LeaderboardResponse();
                    }
                }

                // Validate requested RankBy against allowed views
                var rankBy = request.RankBy.ToLower();
                var showGender = leaderboardSettings?.ShowGenderResults ?? false;
                var showCategory = leaderboardSettings?.ShowCategoryResults ?? false;

                if (rankBy == "gender" && !showGender)
                {
                    rankBy = "overall";
                }
                else if (rankBy == "category" && !showCategory)
                {
                    rankBy = "overall";
                }

                // Build display settings from the loaded configurations
                var displaySettings = BuildDisplaySettings(eventSettings, raceSettings, leaderboardSettings, rankBy);

                var resultsRepo = _repository.GetRepository<Results>();

                // Filter by status based on PublishDnf setting
                IQueryable<Results> query;
                if (displaySettings.ShowDnf)
                {
                    query = resultsRepo.GetQuery(r =>
                        r.EventId == decryptedEventId &&
                        r.RaceId == decryptedRaceId &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                        .Include(r => r.Participant);
                }
                else
                {
                    query = resultsRepo.GetQuery(r =>
                        r.EventId == decryptedEventId &&
                        r.RaceId == decryptedRaceId &&
                        r.Status == "Finished" &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                        .Include(r => r.Participant);
                }

                // Apply filters
                if (!string.IsNullOrEmpty(request.Gender))
                {
                    query = query.Where(r => r.Participant.Gender == request.Gender);
                }

                if (!string.IsNullOrEmpty(request.Category))
                {
                    query = query.Where(r => r.Participant.AgeCategory == request.Category);
                }

                // Determine sort field based on settings
                var useNetTime = displaySettings.RankOnNet;
                query = rankBy switch
                {
                    "gender" => query
                        .OrderBy(r => r.Participant.Gender)
                        .ThenBy(r => r.GenderRank),
                    "category" => query
                        .OrderBy(r => r.Participant.AgeCategory)
                        .ThenBy(r => r.CategoryRank),
                    _ => useNetTime
                        ? query.OrderBy(r => r.NetTime)
                        : query.OrderBy(r => r.OverallRank)
                };

                var totalCount = await query.CountAsync();

                // Apply per-view result limits from leaderboard settings
                int maxResults;
                if (rankBy == "category" && leaderboardSettings?.NumberOfResultsToShowCategory > 0)
                {
                    maxResults = leaderboardSettings.NumberOfResultsToShowCategory.Value;
                }
                else if (leaderboardSettings?.NumberOfResultsToShowOverall > 0)
                {
                    maxResults = leaderboardSettings.NumberOfResultsToShowOverall.Value;
                }
                else
                {
                    maxResults = totalCount;
                }

                // Cap total count to the configured limit
                var effectiveTotalCount = Math.Min(totalCount, maxResults);

                // Apply max displayed records limit to page size
                var effectivePageSize = request.PageSize;
                if (leaderboardSettings?.MaxDisplayedRecords > 0)
                {
                    effectivePageSize = Math.Min(effectivePageSize, leaderboardSettings.MaxDisplayedRecords.Value);
                }

                var totalPages = (int)Math.Ceiling(effectiveTotalCount / (double)effectivePageSize);

                // Ensure requested page doesn't exceed capped total
                var skip = (request.PageNumber - 1) * effectivePageSize;
                var take = Math.Min(effectivePageSize, Math.Max(0, effectiveTotalCount - skip));

                var results = take > 0
                    ? await query.Skip(skip).Take(take).ToListAsync()
                    : new List<Results>();

                var leaderboardEntries = new List<LeaderboardEntry>();

                // Fetch race and event once for pace calculation and naming
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId).FirstOrDefaultAsync();

                var eventRepo = _repository.GetRepository<Event>();
                var @event = await eventRepo.GetQuery(e => e.Id == decryptedEventId).FirstOrDefaultAsync();

                foreach (var result in results)
                {
                    var entry = _mapper.Map<LeaderboardEntry>(result);

                    entry.Rank = rankBy switch
                    {
                        "gender" => result.GenderRank ?? 0,
                        "category" => result.CategoryRank ?? 0,
                        _ => result.OverallRank ?? 0
                    };

                    // Format times based on sort field setting
                    if (displaySettings.SortTimeField == "NetTime")
                    {
                        if (result.NetTime.HasValue) entry.NetTime = FormatTime(result.NetTime.Value);
                        if (result.GunTime.HasValue) entry.GunTime = FormatTime(result.GunTime.Value);
                    }
                    else
                    {
                        if (result.GunTime.HasValue) entry.GunTime = FormatTime(result.GunTime.Value);
                        if (result.NetTime.HasValue) entry.NetTime = FormatTime(result.NetTime.Value);
                    }
                    if (result.FinishTime.HasValue) entry.FinishTime = FormatTime(result.FinishTime.Value);

                    // Calculate average pace if enabled in settings and we have the data
                    if (displaySettings.ShowPace && race != null && result.FinishTime.HasValue && race.Distance > 0)
                    {
                        var timeInMinutes = result.FinishTime.Value / 60000.0m;
                        entry.AveragePace = timeInMinutes / race.Distance;
                        entry.AveragePaceFormatted = FormatPace(entry.AveragePace.Value);
                    }

                    // Strip rankings the admin has disabled
                    if (!displaySettings.ShowGenderResults)
                    {
                        entry.GenderRank = null;
                    }
                    if (!displaySettings.ShowCategoryResults)
                    {
                        entry.CategoryRank = null;
                    }

                    // Include splits if requested and enabled in settings
                    if (request.IncludeSplits && displaySettings.ShowSplitTimes)
                    {
                        entry.Splits = await GetParticipantSplitsAsync(result.ParticipantId, decryptedEventId);
                    }

                    leaderboardEntries.Add(entry);
                }

                return new LeaderboardResponse
                {
                    TotalCount = effectiveTotalCount,
                    Page = request.PageNumber,
                    PageSize = effectivePageSize,
                    TotalPages = totalPages,
                    RankBy = rankBy,
                    Gender = request.Gender,
                    Category = request.Category,
                    EventName = @event?.Name,
                    RaceName = race?.Title,
                    Results = leaderboardEntries,
                    DisplaySettings = displaySettings
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error retrieving leaderboard: {ex.Message}";
                _logger.LogError(ex, "Error retrieving leaderboard");
                return new LeaderboardResponse();
            }
        }

        public async Task<ParticipantResultResponse?> GetParticipantResultAsync(
            string eventId,
            string raceId,
            string participantId)
        {
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));

            try
            {
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var participant = await participantRepo.GetQuery(p =>
                    p.Id == decryptedParticipantId &&
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (participant == null)
                {
                    ErrorMessage = "Participant not found";
                    return null;
                }

                var resultsRepo = _repository.GetRepository<Results>();
                var result = await resultsRepo.GetQuery(r =>
                    r.ParticipantId == decryptedParticipantId &&
                    r.EventId == decryptedEventId &&
                    r.RaceId == decryptedRaceId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                var response = new ParticipantResultResponse
                {
                    Participant = new ParticipantInfo
                    {
                        ParticipantId = participantId,
                        Bib = participant.BibNumber ?? string.Empty,
                        FirstName = participant.FirstName ?? string.Empty,
                        LastName = participant.LastName ?? string.Empty,
                        Email = participant.Email ?? string.Empty,
                        Phone = participant.Phone,
                        Gender = participant.Gender ?? string.Empty,
                        Category = participant.AgeCategory,
                        Age = participant.Age,
                        City = participant.City,
                        State = participant.State
                    },
                    Splits = await GetParticipantSplitsAsync(decryptedParticipantId, decryptedEventId)
                };

                if (result != null)
                {
                    response.Result = new ResultInfo
                    {
                        ResultId = _encryptionService.Encrypt(result.Id.ToString()),
                        FinishTimeMs = result.FinishTime,
                        GunTimeMs = result.GunTime,
                        NetTimeMs = result.NetTime,
                        FinishTime = result.FinishTime.HasValue ? FormatTime(result.FinishTime.Value) : null,
                        GunTime = result.GunTime.HasValue ? FormatTime(result.GunTime.Value) : null,
                        NetTime = result.NetTime.HasValue ? FormatTime(result.NetTime.Value) : null,
                        OverallRank = result.OverallRank,
                        GenderRank = result.GenderRank,
                        CategoryRank = result.CategoryRank,
                        Status = result.Status,
                        DisqualificationReason = result.DisqualificationReason,
                        IsOfficial = result.IsOfficial,
                        CertificateGenerated = result.CertificateGenerated
                    };

                    // Calculate average pace
                    var raceRepo = _repository.GetRepository<Race>();
                    var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId).FirstOrDefaultAsync();
                    if (race != null && result.FinishTime.HasValue && race.Distance > 0)
                    {
                        var timeInMinutes = result.FinishTime.Value / 60000.0m;
                        response.Result.AveragePace = timeInMinutes / race.Distance;
                        response.Result.AveragePaceFormatted = FormatPace(response.Result.AveragePace.Value);
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error retrieving participant result: {ex.Message}";
                _logger.LogError(ex, "Error retrieving participant result");
                return null;
            }
        }

        public async Task<ParticipantDetailsResponse?> GetParticipantDetailsAsync(
            string eventId,
            string raceId,
            string participantId)
        {
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));

            try
            {
                // 1. Load participant with navigation properties
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var participant = await participantRepo.GetQuery(p =>
                    p.Id == decryptedParticipantId &&
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .Include(p => p.Event)
                    .Include(p => p.Race)
                    .Include(p => p.Result)
                    .Include(p => p.ChipAssignments)
                        .ThenInclude(ca => ca.Chip)
                    .FirstOrDefaultAsync();

                if (participant == null)
                {
                    ErrorMessage = "Participant not found";
                    return null;
                }

                // 2. Load split times ordered by checkpoint distance
                var splitTimeRepo = _repository.GetRepository<SplitTimes>();
                var splitTimes = await splitTimeRepo.GetQuery(st =>
                    st.ParticipantId == decryptedParticipantId &&
                    st.EventId == decryptedEventId &&
                    st.AuditProperties.IsActive &&
                    !st.AuditProperties.IsDeleted)
                    .Include(st => st.ToCheckpoint)
                    .OrderBy(st => st.ToCheckpoint.DistanceFromStart)
                    .ToListAsync();

                // 3. Get ranking totals for each group
                var resultsRepo = _repository.GetRepository<Results>();

                var totalFinished = await resultsRepo.CountAsync(r =>
                    r.RaceId == decryptedRaceId &&
                    r.Status == "Finished" &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted);

                int totalInGender = 0;
                if (!string.IsNullOrEmpty(participant.Gender))
                {
                    totalInGender = await resultsRepo.GetQuery(r =>
                        r.RaceId == decryptedRaceId &&
                        r.Status == "Finished" &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                        .Include(r => r.Participant)
                        .CountAsync(r => r.Participant.Gender == participant.Gender);
                }

                int totalInCategory = 0;
                if (!string.IsNullOrEmpty(participant.AgeCategory))
                {
                    totalInCategory = await resultsRepo.GetQuery(r =>
                        r.RaceId == decryptedRaceId &&
                        r.Status == "Finished" &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                        .Include(r => r.Participant)
                        .CountAsync(r => r.Participant.AgeCategory == participant.AgeCategory);
                }

                // 4. Build base response using existing builder
                var detailsBuilder = new ParticipantDetailsResponseBuilder(_mapper);
                var response = detailsBuilder.BuildResponse(
                    participant, splitTimes, totalFinished, totalInGender, totalInCategory);

                // 5. Load checkpoint times from ReadNormalized with dynamically calculated rankings
                response.CheckpointTimes = await LoadCheckpointTimesAsync(
                    decryptedParticipantId, decryptedEventId, decryptedRaceId,
                    participant.Event?.TimeZone);

                // 6. Get EPC from active chip assignment
                var epc = participant.ChipAssignments
                    .Where(ca => ca.UnassignedAt == null)
                    .OrderByDescending(ca => ca.AssignedAt)
                    .FirstOrDefault()?.Chip?.EPC;
                response.Epc = epc;

                // 7. Load RFID readings from ReadNormalized
                response.RfidReadings = await LoadRfidReadingsAsync(
                    decryptedParticipantId, decryptedEventId, participant.Event?.TimeZone);

                // Set chip ID on each reading from the active chip assignment
                if (!string.IsNullOrEmpty(epc))
                {
                    foreach (var reading in response.RfidReadings)
                    {
                        reading.ChipId = epc;
                    }
                }

                // 8. Load ALL raw tag detections (every antenna ping, including duplicates)
                response.RawRfidTagReadings = !string.IsNullOrEmpty(epc)
                    ? await LoadRawRfidReadingsAsync(epc, decryptedParticipantId, decryptedRaceId, decryptedEventId, participant.Event?.TimeZone)
                    : [];

                response.ProcessingNotes = response.RfidReadings
                    .Where(r => !string.IsNullOrEmpty(r.Notes))
                    .Select(r => r.Notes!)
                    .ToList();

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error retrieving participant details: {ex.Message}";
                _logger.LogError(ex, "Error retrieving participant details for participant {ParticipantId}", participantId);
                return null;
            }
        }

        #region Private Helper Methods

        private async Task<List<CheckpointTimeInfo>> LoadCheckpointTimesAsync(
            int participantId, int eventId, int raceId, string? eventTimeZone)
        {
            var checkpointTimeInfos = new List<CheckpointTimeInfo>();

            var checkpointRepo = _repository.GetRepository<Checkpoint>();
            // Display-only: show PARENT/primary checkpoints only. A child (paired-reader) checkpoint
            // has ParentDeviceId set (> 0) and no Name; its reads are merged into the parent during
            // normalization (RFIDImportService.cs:1824/2358), so it carries no distinct timing for the
            // table and would render as a blank "-" duplicate row at the same distance. This filters the
            // RESULTS DISPLAY only — it does NOT affect timing/normalization/ranking.
            var checkpoints = await checkpointRepo.GetQuery(c =>
                c.RaceId == raceId &&
                c.EventId == eventId &&
                (c.ParentDeviceId == null || c.ParentDeviceId == 0) &&
                c.AuditProperties.IsActive &&
                !c.AuditProperties.IsDeleted)
                .OrderBy(c => c.DistanceFromStart)
                .AsNoTracking()
                .ToListAsync();

            if (checkpoints.Count == 0)
                return checkpointTimeInfos;

            // VALID START WINDOW (display): the start checkpoint shows "not found" (blank, like a
            // not-crossed gate) when the participant's start read is OUTSIDE
            // [gun - EarlyStartCutOff, gun + LateStartCutOff] (SECONDS; defaults 300/1200) — mirrors
            // the status rule in RFIDImportService Phase 3.
            var startCheckpointId = checkpoints[0].Id; // ordered by DistanceFromStart asc → start gate
            var raceForWindow = await _repository.GetRepository<Race>().GetQuery(r =>
                    r.Id == raceId && r.EventId == eventId)
                .Include(r => r.RaceSettings)
                .AsNoTracking()
                .FirstOrDefaultAsync();
            // SAME window computation as status (RFIDImportService Phase 3) via the shared helper —
            // status and display cannot drift.
            var (validStartFloor, validStartCeiling) =
                StartWindow.For(raceForWindow?.StartTime, raceForWindow?.RaceSettings?.EarlyStartCutOff, raceForWindow?.RaceSettings?.LateStartCutOff);

            var normalizedRepo = _repository.GetRepository<ReadNormalized>();
            var allReadings = await normalizedRepo.GetQuery(r =>
                r.EventId == eventId &&
                r.Participant.RaceId == raceId &&
                r.AuditProperties.IsActive &&
                !r.AuditProperties.IsDeleted)
                .Include(r => r.Participant)
                .AsNoTracking()
                .ToListAsync();

            var rankedByCheckpoint = allReadings
                .GroupBy(r => r.CheckpointId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(r => r.ParticipantId)
                          .Select(pg => pg.OrderBy(r => r.ChipTime).First())
                          .OrderBy(r => r.GunTime ?? long.MaxValue)
                          .ToList());

            var currentParticipant = allReadings
                .FirstOrDefault(r => r.ParticipantId == participantId)?.Participant;

            TimeZoneInfo timeZone;
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZone ?? "India Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }

            foreach (var checkpoint in checkpoints)
            {
                var info = new CheckpointTimeInfo
                {
                    CheckpointId = _encryptionService.Encrypt(checkpoint.Id.ToString()),
                    CheckpointName = checkpoint.Name ?? $"CP {checkpoint.DistanceFromStart}",
                    DistanceKm = checkpoint.DistanceFromStart
                };

                if (rankedByCheckpoint.TryGetValue(checkpoint.Id, out var sortedReadings))
                {
                    var participantReading = sortedReadings
                        .FirstOrDefault(r => r.ParticipantId == participantId);

                    // Start gate with an OUT-OF-WINDOW read → no valid start crossing → render as
                    // "not found" (leave Time/ranks unpopulated, same as a not-crossed gate).
                    var startOutOfWindow = participantReading != null &&
                        checkpoint.Id == startCheckpointId && validStartFloor.HasValue &&
                        (participantReading.ChipTime < validStartFloor.Value ||
                         participantReading.ChipTime > validStartCeiling!.Value);

                    if (participantReading != null && !startOutOfWindow)
                    {
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(participantReading.ChipTime, timeZone);
                        info.Time = localTime.ToString("HH:mm:ss");
                        // Full event-local crossing datetime so the manual-time editor defaults to the
                        // ACTUAL crossing date (e.g. the next day for a near-midnight gun), not the race start.
                        info.LocalDateTime = localTime.ToString("yyyy-MM-ddTHH:mm:ss");

                        info.OverallRank = sortedReadings
                            .Select((r, idx) => new { r.ParticipantId, Rank = idx + 1 })
                            .First(x => x.ParticipantId == participantId).Rank;

                        if (currentParticipant != null && !string.IsNullOrEmpty(currentParticipant.Gender))
                        {
                            var genderRank = 1;
                            foreach (var r in sortedReadings)
                            {
                                if (r.ParticipantId == participantId) break;
                                if (r.Participant.Gender == currentParticipant.Gender) genderRank++;
                            }
                            info.GenderRank = genderRank;
                        }

                        if (currentParticipant != null && !string.IsNullOrEmpty(currentParticipant.AgeCategory))
                        {
                            var categoryRank = 1;
                            foreach (var r in sortedReadings)
                            {
                                if (r.ParticipantId == participantId) break;
                                if (r.Participant.AgeCategory == currentParticipant.AgeCategory) categoryRank++;
                            }
                            info.CategoryRank = categoryRank;
                        }
                    }
                }

                checkpointTimeInfos.Add(info);
            }

            return checkpointTimeInfos;
        }

        private async Task<List<RfidReadingDetail>> LoadRfidReadingsAsync(
            int participantId, int eventId, string? eventTimeZone)
        {
            var normalizedRepo = _repository.GetRepository<ReadNormalized>();

            var readings = await normalizedRepo.GetQuery(r =>
                r.ParticipantId == participantId &&
                r.EventId == eventId &&
                r.AuditProperties.IsActive &&
                !r.AuditProperties.IsDeleted)
                .Include(r => r.Checkpoint)
                    .ThenInclude(c => c.Device)
                .OrderBy(r => r.CheckpointId)
                .ToListAsync();

            var result = _mapper.Map<List<RfidReadingDetail>>(readings);

            TimeZoneInfo timeZone;
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZone ?? "India Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }

            for (int i = 0; i < readings.Count; i++)
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(readings[i].ChipTime, timeZone);
                result[i].ReadTimeLocal = localTime.ToString("HH:mm:ss");

                if (readings[i].GunTime.HasValue)
                {
                    result[i].GunTimeFormatted = TimeFormatter.FormatTimeSpan(readings[i].GunTime.Value);
                }
                if (readings[i].NetTime.HasValue)
                {
                    result[i].NetTimeFormatted = TimeFormatter.FormatTimeSpan(readings[i].NetTime.Value);
                }
            }

            return result;
        }

        private async Task<List<RfidRawReadingDto>> LoadRawRfidReadingsAsync(
            string chipEpc, int participantId, int raceId, int eventId, string? eventTimeZone)
        {
            var rawRepo = _repository.GetRepository<RawRFIDReading>();

            var readings = await rawRepo.GetQuery(r =>
                r.Epc == chipEpc &&
                r.UploadBatch.EventId == eventId &&
                r.AuditProperties.IsActive &&
                !r.AuditProperties.IsDeleted)
                .Include(r => r.UploadBatch)
                    .ThenInclude(b => b.ReaderDevice)
                .Include(r => r.ReadingCheckpointAssignments)
                    .ThenInclude(a => a.Checkpoint)
                .OrderBy(r => r.ReadTimeUtc)
                .AsNoTracking()
                .ToListAsync();

            // Build RawReadId → normalized record lookup to identify winning reads and get gun/net times
            var normalizedRepo = _repository.GetRepository<ReadNormalized>();
            var normalizedByRawId = (await normalizedRepo.GetQuery(n =>
                n.ParticipantId == participantId &&
                n.EventId == eventId &&
                n.AuditProperties.IsActive &&
                !n.AuditProperties.IsDeleted)
                .Where(n => n.RawReadId != null)
                .AsNoTracking()
                .ToListAsync())
                .GroupBy(n => n.RawReadId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            // Checkpoints (for this participant) that currently have an ACTIVE manual override. Keyed
            // off the override ROW — the single source of truth — so the "is the current pick an
            // override or the dedup default?" signal never depends on IsManualEntry.
            var overrideCheckpointIds = (await _repository.GetRepository<ManualTimeOverride>()
                .GetQuery(o =>
                    o.ParticipantId == participantId &&
                    o.AuditProperties.IsActive &&
                    !o.AuditProperties.IsDeleted)
                .AsNoTracking()
                .Select(o => o.CheckpointId)
                .ToListAsync())
                .ToHashSet();

            // Build DistanceFromStart → name lookup for parent (named) checkpoints.
            // Child checkpoints share the same distance as their parent but have empty names.
            var checkpointRepo = _repository.GetRepository<Checkpoint>();
            var namedByDistance = (await checkpointRepo.GetQuery(c =>
                c.RaceId == raceId &&
                c.EventId == eventId &&
                c.Name != null && c.Name != "" &&
                c.AuditProperties.IsActive &&
                !c.AuditProperties.IsDeleted)
                .AsNoTracking()
                .ToListAsync())
                .GroupBy(c => c.DistanceFromStart)
                .ToDictionary(g => g.Key, g => g.First().Name!);

            TimeZoneInfo timeZone;
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZone ?? "India Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }

            var result = new List<RfidRawReadingDto>(readings.Count);
            foreach (var r in readings)
            {
                var assignment = r.ReadingCheckpointAssignments
                    .Where(a => a.AuditProperties.IsActive && !a.AuditProperties.IsDeleted)
                    .FirstOrDefault();

                var localTime = TimeZoneInfo.ConvertTimeFromUtc(r.ReadTimeUtc, timeZone);
                var isNormalized = normalizedByRawId.TryGetValue(r.Id, out var normalized);

                string? checkpointDisplay = null;
                if (assignment?.Checkpoint != null)
                {
                    var cp = assignment.Checkpoint;
                    var dist = cp.DistanceFromStart;
                    var name = string.IsNullOrEmpty(cp.Name)
                        ? namedByDistance.GetValueOrDefault(dist)
                        : cp.Name;
                    if (name != null)
                        checkpointDisplay = $"{name} ({dist:0.##} km)";
                }

                result.Add(new RfidRawReadingDto
                {
                    Id = r.Id.ToString(),
                    LocalTime = localTime.ToString("HH:mm:ss"),
                    Date = localTime.ToString("yyyy-MM-dd"),
                    Checkpoint = checkpointDisplay,
                    CheckpointId = assignment?.Checkpoint != null
                        ? _encryptionService.Encrypt(assignment.Checkpoint.Id.ToString())
                        : null,
                    HasActiveOverride = assignment?.Checkpoint != null
                        && overrideCheckpointIds.Contains(assignment.Checkpoint.Id),
                    CheckpointDistance = assignment?.Checkpoint?.DistanceFromStart,
                    Device = r.UploadBatch?.ReaderDevice?.Name ?? r.DeviceId,
                    DeviceId = r.DeviceId,
                    GunTime = isNormalized && normalized?.GunTime is { } gv
                        ? TimeFormatter.FormatTimeSpan(gv) : null,
                    NetTime = isNormalized && normalized?.NetTime is { } nv
                        ? TimeFormatter.FormatTimeSpan(nv) : null,
                    ChipId = r.Epc,
                    ProcessResult = r.ProcessResult,
                    IsManual = r.IsManualEntry,
                    IsDuplicate = r.ProcessResult == "Duplicate" || r.DuplicateOfReadingId.HasValue,
                    IsNormalized = isNormalized,
                    IsMultipleEpc = r.IsMultipleEpc
                });
            }

            return result;
        }

        private static LeaderboardDisplaySettings BuildDisplaySettings(
            EventSettings? eventSettings,
            RaceSettings? raceSettings,
            LeaderboardSettings? leaderboardSettings,
            string rankBy)
        {
            var display = new LeaderboardDisplaySettings();

            if (eventSettings != null)
            {
                display.RankOnNet = eventSettings.RankOnNet;
            }

            if (raceSettings != null)
            {
                display.ShowDnf = raceSettings.PublishDnf;
            }

            if (leaderboardSettings != null)
            {
                display.ShowOverallResults = leaderboardSettings.ShowOverallResults ?? true;
                display.ShowCategoryResults = leaderboardSettings.ShowCategoryResults ?? false;
                display.ShowGenderResults = leaderboardSettings.ShowGenderResults ?? false;
                display.ShowAgeGroupResults = leaderboardSettings.ShowAgeGroupResults ?? false;
                display.ShowSplitTimes = leaderboardSettings.ShowSplitTimes ?? false;
                display.ShowPace = leaderboardSettings.ShowPace ?? false;
                display.ShowMedalIcon = leaderboardSettings.ShowMedalIcon ?? false;
                display.MaxResultsOverall = leaderboardSettings.NumberOfResultsToShowOverall;
                display.MaxResultsCategory = leaderboardSettings.NumberOfResultsToShowCategory;
                display.MaxDisplayedRecords = leaderboardSettings.MaxDisplayedRecords;

                display.SortTimeField = rankBy switch
                {
                    "category" => leaderboardSettings.SortByCategoryChipTime == true ? "NetTime" : "GunTime",
                    _ => leaderboardSettings.SortByOverallChipTime == true ? "NetTime" : "GunTime"
                };
            }

            if (display.RankOnNet)
            {
                display.SortTimeField = "NetTime";
            }

            return display;
        }

        private async Task<List<ResultsSplitTimeInfo>> GetParticipantSplitsAsync(int participantId, int eventId)
        {
            var splitTimeRepo = _repository.GetRepository<SplitTimes>();
            var splits = await splitTimeRepo.GetQuery(st =>
                st.ParticipantId == participantId &&
                st.EventId == eventId &&
                st.AuditProperties.IsActive &&
                !st.AuditProperties.IsDeleted)
                .Include(st => st.Checkpoint)
                .OrderBy(st => st.Checkpoint.DistanceFromStart)
                .ToListAsync();

            var splitInfos = _mapper.Map<List<ResultsSplitTimeInfo>>(splits);

            var startSplitTimeMs = splits.Count > 0 ? (splits[0].SplitTimeMs ?? 0L) : 0L;

            for (int i = 0; i < splits.Count; i++)
            {
                var raw = splits[i];

                // SplitTime = interval between consecutive checkpoints (SegmentTime), not cumulative
                splitInfos[i].SplitTime = raw.SegmentTime.HasValue
                    ? FormatTime(raw.SegmentTime.Value)
                    : FormatTime(raw.SplitTimeMs ?? 0);

                // SegmentTime = same value (kept for backward compatibility)
                splitInfos[i].SegmentTime = raw.SegmentTime.HasValue
                    ? FormatTime(raw.SegmentTime.Value)
                    : null;

                // CumulativeTime = elapsed from start-line crossing
                // Start row: show its own SplitTimeMs (the gun-to-start delay)
                // All others: SplitTimeMs - startSplitTimeMs
                var cumulativeMs = i == 0
                    ? (raw.SplitTimeMs ?? 0L)
                    : (raw.SplitTimeMs ?? 0L) - startSplitTimeMs;
                splitInfos[i].CumulativeTimeMs = cumulativeMs;
                splitInfos[i].CumulativeTime = FormatTime(cumulativeMs);

                splitInfos[i].PaceFormatted = raw.Pace.HasValue ? FormatPace(raw.Pace.Value) : null;
            }

            return splitInfos;
        }

        private async Task CalculateSplitTimeRankingsAsync(int eventId, int raceId, int userId)
        {
            var splitTimeRepo = _repository.GetRepository<SplitTimes>();
            var checkpointRepo = _repository.GetRepository<Checkpoint>();

            var checkpoints = await checkpointRepo.GetQuery(c =>
                c.RaceId == raceId &&
                c.EventId == eventId &&
                c.AuditProperties.IsActive &&
                !c.AuditProperties.IsDeleted)
                .ToListAsync();

            foreach (var checkpoint in checkpoints)
            {
                var splits = await splitTimeRepo.GetQuery(st =>
                    st.EventId == eventId &&
                    st.CheckpointId == checkpoint.Id &&
                    st.Participant.RaceId == raceId &&
                    st.AuditProperties.IsActive &&
                    !st.AuditProperties.IsDeleted)
                    .Include(st => st.Participant)
                    .OrderBy(st => st.SplitTimeMs)
                    .ToListAsync();

                var rank = 1;
                foreach (var split in splits)
                {
                    split.Rank = rank++;
                    split.AuditProperties.UpdatedBy = userId;
                    split.AuditProperties.UpdatedDate = DateTime.UtcNow;
                }

                foreach (var gender in new[] { "M", "F" })
                {
                    var genderSplits = splits.Where(s => s.Participant.Gender == gender).ToList();
                    rank = 1;
                    foreach (var split in genderSplits)
                    {
                        split.GenderRank = rank++;
                    }
                }

                var categories = splits.Select(s => s.Participant.AgeCategory).Distinct().Where(c => !string.IsNullOrEmpty(c));
                foreach (var category in categories)
                {
                    var categorySplits = splits.Where(s => s.Participant.AgeCategory == category).ToList();
                    rank = 1;
                    foreach (var split in categorySplits)
                    {
                        split.CategoryRank = rank++;
                    }
                }

                await splitTimeRepo.UpdateRangeAsync(splits);
            }

            await _repository.SaveChangesAsync();
        }

        private async Task CalculateResultRankingsAsync(int eventId, int raceId, int userId)
        {
            var resultsRepo = _repository.GetRepository<Results>();
            var results = await resultsRepo.GetQuery(r =>
                r.EventId == eventId &&
                r.RaceId == raceId &&
                r.Status == ResultStatus.Finished &&
                r.FinishTime.HasValue &&
                r.AuditProperties.IsActive &&
                !r.AuditProperties.IsDeleted)
                .Include(r => r.Participant)
                .AsNoTracking()
                .OrderBy(r => r.FinishTime)
                .ToListAsync();

            var rank = 1;
            foreach (var result in results)
            {
                result.OverallRank = rank++;
                result.AuditProperties.UpdatedBy = userId;
                result.AuditProperties.UpdatedDate = DateTime.UtcNow;
            }

            foreach (var gender in new[] { "M", "F" })
            {
                var genderResults = results.Where(r => r.Participant.Gender == gender).ToList();
                rank = 1;
                foreach (var result in genderResults)
                {
                    result.GenderRank = rank++;
                }
            }

            var categories = results.Select(r => r.Participant.AgeCategory).Distinct().Where(c => !string.IsNullOrEmpty(c));
            foreach (var category in categories)
            {
                var categoryResults = results.Where(r => r.Participant.AgeCategory == category).ToList();
                rank = 1;
                foreach (var result in categoryResults)
                {
                    result.CategoryRank = rank++;
                }
            }

            await resultsRepo.BulkUpdateAsync(results);
        }

        private static string FormatTime(long milliseconds)
        {
            var timeSpan = TimeSpan.FromMilliseconds(milliseconds);
            return timeSpan.ToString(@"hh\:mm\:ss");
        }

        private static string FormatPace(decimal paceMinPerKm)
        {
            var totalSeconds = (int)(paceMinPerKm * 60);
            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return $"{minutes}:{seconds:D2} min/km";
        }

        #endregion

        public async Task<ManualTimeResponse?> RecordManualTimeAsync(
            string eventId,
            string raceId,
            string participantId,
            long? finishTimeMs,
            string checkpointId,
            string? crossingLocalDateTime = null,
            string? chosenRawReadId = null)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));
            var decryptedCheckpointId = Convert.ToInt32(_encryptionService.Decrypt(checkpointId));

            try
            {
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var participant = await participantRepo.GetQuery(p =>
                    p.Id == decryptedParticipantId &&
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (participant == null)
                {
                    ErrorMessage = "Participant not found";
                    return null;
                }

                // Load Race for UTC gun start time and IsTimed flag
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (race == null || !race.StartTime.HasValue)
                {
                    ErrorMessage = "Race not found or Race.StartTime is not configured. Cannot compute chip time.";
                    return null;
                }

                // BUG-14: for a Timed race, manual time must not be allowed unless the participant has an
                // EPC chip mapped (an active ChipAssignment). Applies to every checkpoint.
                if (race.IsTimed)
                {
                    var hasEpcMapped = await _repository.GetRepository<ChipAssignment>()
                        .GetQuery(ca => ca.ParticipantId == decryptedParticipantId
                                     && ca.UnassignedAt == null
                                     && ca.AuditProperties.IsActive
                                     && !ca.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .AnyAsync();

                    if (!hasEpcMapped)
                    {
                        ErrorMessage = "Map an EPC chip to this participant before recording a manual time for a timed race.";
                        return null;
                    }
                }

                // Load all race checkpoints ordered by distance (start → finish)
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var raceCheckpoints = await checkpointRepo.GetQuery(c =>
                    c.RaceId == decryptedRaceId &&
                    c.AuditProperties.IsActive &&
                    !c.AuditProperties.IsDeleted)
                    .OrderBy(c => c.DistanceFromStart)
                    .AsNoTracking()
                    .ToListAsync();

                var editedIndex = raceCheckpoints.FindIndex(c => c.Id == decryptedCheckpointId);
                if (editedIndex < 0)
                {
                    ErrorMessage = $"Checkpoint {decryptedCheckpointId} not found for this race.";
                    return null;
                }

                var editedCheckpoint = raceCheckpoints[editedIndex];
                var isFinish = editedIndex == raceCheckpoints.Count - 1;

                var raceStartUtc = race.StartTime.Value;

                // Resolve the event's local timezone — the SAME source the display path
                // (LoadCheckpointTimesAsync) and automatic reads (RFIDImportService.ParseSqliteFileAsync)
                // use — so manual entry round-trips identically. Fall back to IST if the id is unknown.
                var eventTimeZoneId = await _repository.GetRepository<Models.Data.Entities.Event>()
                    .GetQuery(e => e.Id == decryptedEventId)
                    .Select(e => e.TimeZone)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                TimeZoneInfo eventTz;
                try
                {
                    eventTz = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZoneId ?? "India Standard Time");
                }
                catch (TimeZoneNotFoundException)
                {
                    eventTz = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
                }

                // crossingUtc is the absolute UTC instant of the crossing (stored on ReadNormalized.ChipTime,
                // exactly like an automatic read's ReadTimeUtc); chipTimeMs is elapsed-from-gun (Gun/Net/Split).
                long chipTimeMs;
                DateTime crossingUtc;

                // chosenReadId is set only on the CHOSEN-READ path (operator picked an existing
                // hardware read). It flows through to STEP A-1 (override.ChosenRawReadId) and STEP A0
                // (ReadNormalized.RawReadId + IsManualEntry=false). NULL ⇒ typed manual time.
                long? chosenReadId = null;

                if (!string.IsNullOrWhiteSpace(chosenRawReadId))
                {
                    // CHOSEN READ: the crossing IS an existing raw read's time. Validate server-side —
                    // never trust the client. The read must (a) exist & be live, (b) be assigned to THIS
                    // checkpoint (enforces "same checkpoint only"), and (c) belong to this participant's chip.
                    if (!long.TryParse(chosenRawReadId, out var parsedReadId))
                    {
                        ErrorMessage = $"Invalid ChosenRawReadId '{chosenRawReadId}'.";
                        return null;
                    }

                    var rawReadRepo = _repository.GetRepository<RawRFIDReading>();
                    var chosenRead = await rawReadRepo.GetQuery(r =>
                        r.Id == parsedReadId &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                        .Include(r => r.ReadingCheckpointAssignments)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();

                    if (chosenRead == null)
                    {
                        ErrorMessage = "Selected read not found.";
                        return null;
                    }

                    var assignedToCheckpoint = chosenRead.ReadingCheckpointAssignments.Any(a =>
                        a.CheckpointId == decryptedCheckpointId &&
                        a.AuditProperties.IsActive && !a.AuditProperties.IsDeleted);
                    if (!assignedToCheckpoint)
                    {
                        ErrorMessage = "Selected read is not assigned to this checkpoint. A read can only be chosen for its own checkpoint.";
                        return null;
                    }

                    var participantEpcs = await _repository.GetRepository<ChipAssignment>()
                        .GetQuery(ca =>
                            ca.ParticipantId == decryptedParticipantId &&
                            ca.UnassignedAt == null &&
                            ca.AuditProperties.IsActive && !ca.AuditProperties.IsDeleted)
                        .Select(ca => ca.Chip.EPC)
                        .AsNoTracking()
                        .ToListAsync();
                    if (!participantEpcs.Contains(chosenRead.Epc))
                    {
                        ErrorMessage = "Selected read does not belong to this participant.";
                        return null;
                    }

                    chosenReadId = parsedReadId;
                    crossingUtc = chosenRead.ReadTimeUtc;
                    chipTimeMs = (long)(crossingUtc - raceStartUtc).TotalMilliseconds;
                }
                else if (!string.IsNullOrWhiteSpace(crossingLocalDateTime))
                {
                    // Preferred: a wall-clock crossing date+time in the event-local zone (no offset).
                    // Convert local -> UTC via Event.TimeZone, then derive elapsed-from-gun. Carries the
                    // calendar date so midnight-crossing reads are unambiguous.
                    if (!DateTime.TryParse(crossingLocalDateTime, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var localDt))
                    {
                        ErrorMessage = $"Could not parse CrossingLocalDateTime '{crossingLocalDateTime}'. Expected a local date+time like '2026-05-10T08:39:15'.";
                        return null;
                    }

                    crossingUtc = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified), eventTz);
                    chipTimeMs = (long)(crossingUtc - raceStartUtc).TotalMilliseconds;
                }
                else if (finishTimeMs.HasValue && finishTimeMs.Value > 0 && finishTimeMs.Value < 86_400_000)
                {
                    // Legacy fallback: elapsed ms from race start (no date supplied).
                    chipTimeMs = finishTimeMs.Value;
                    crossingUtc = raceStartUtc.AddMilliseconds(chipTimeMs);
                }
                else if (finishTimeMs.HasValue)
                {
                    // Legacy fallback: ms-from-midnight in the event-local zone (now uses Event.TimeZone
                    // instead of a hardcoded IST zone).
                    var raceStartLocal = TimeZoneInfo.ConvertTimeFromUtc(raceStartUtc, eventTz);
                    var finishLocal = raceStartLocal.Date.AddMilliseconds(finishTimeMs.Value);
                    crossingUtc = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(finishLocal, DateTimeKind.Unspecified), eventTz);
                    chipTimeMs = (long)(crossingUtc - raceStartUtc).TotalMilliseconds;
                }
                else
                {
                    ErrorMessage = "Provide CrossingLocalDateTime (preferred) or FinishTimeMs to record a manual time.";
                    return null;
                }

                if (chipTimeMs <= 0 || chipTimeMs > 86_400_000)
                {
                    ErrorMessage = $"Calculated chip time {chipTimeMs}ms is invalid. Check that finish time is after race start.";
                    return null;
                }

                var resultsRepo = _repository.GetRepository<Results>();
                var splitRepo = _repository.GetRepository<SplitTimes>();
                var rnRepo = _repository.GetRepository<ReadNormalized>();
                var overrideRepo = _repository.GetRepository<ManualTimeOverride>();

                // BUG-26: mandatory evaluation is per-DISTANCE, not per-checkpoint-ID. Two devices
                // at the same DistanceFromStart are ONE logical gate — a detection at ANY checkpoint
                // at that distance satisfies it. Mirrors RFIDImportService.CalculateRaceResultsAsync
                // and ComputeParticipantStatusAsync. (raceCheckpoints already holds all active
                // checkpoints for this race — no extra query needed.)
                var mandatoryDistances = raceCheckpoints.Any(c => c.IsMandatory)
                    ? raceCheckpoints.Where(c => c.IsMandatory).Select(c => c.DistanceFromStart).Distinct().ToList()
                    // Fallback: the highest distance acts as the mandatory finish gate
                    : new List<decimal> { raceCheckpoints.Max(c => c.DistanceFromStart) };

                var idsByMandatoryDistance = mandatoryDistances.ToDictionary(
                    d => d,
                    d => raceCheckpoints.Where(c => c.DistanceFromStart == d).Select(c => c.Id).ToHashSet());

                var mandatoryGateIds = idsByMandatoryDistance.Values.SelectMany(ids => ids).Distinct().ToList();

                // Existing ReadNormalized detections at the mandatory gates for this participant
                var existingDetectedIds = await rnRepo.GetQuery(rn =>
                    rn.ParticipantId == decryptedParticipantId &&
                    mandatoryGateIds.Contains(rn.CheckpointId) &&
                    !rn.AuditProperties.IsDeleted)
                    .Select(rn => rn.CheckpointId)
                    .Distinct()
                    .ToListAsync();

                // Add the checkpoint being recorded now to the covered set (in-memory, before DB write)
                var coveredCheckpointIds = existingDetectedIds.ToHashSet();
                coveredCheckpointIds.Add(decryptedCheckpointId);

                var computedStatus = mandatoryDistances.All(d => idsByMandatoryDistance[d].Overlaps(coveredCheckpointIds))
                    ? ResultStatus.Finished
                    : ResultStatus.DNF;

                // Try to find existing SplitTimes row (not required — will upsert below)
                var existingSplitForSegment = await splitRepo.GetQuery(s =>
                    s.ParticipantId == decryptedParticipantId &&
                    s.CheckpointId == decryptedCheckpointId &&
                    !s.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                // Determine FromCheckpointId: from existing row, or infer from checkpoint order
                var fromCheckpointId = existingSplitForSegment?.FromCheckpointId
                    ?? (editedIndex > 0 ? raceCheckpoints[editedIndex - 1].Id : decryptedCheckpointId);
                long? previousCumulativeMs = null;
                if (fromCheckpointId != decryptedCheckpointId)
                {
                    var prevSplit = await splitRepo.GetQuery(s =>
                        s.ParticipantId == decryptedParticipantId &&
                        s.CheckpointId == fromCheckpointId &&
                        !s.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();
                    previousCumulativeMs = prevSplit?.SplitTimeMs;
                }

                var segmentTimeMs = previousCumulativeMs.HasValue
                    ? chipTimeMs - previousCumulativeMs.Value
                    : chipTimeMs;

                // Pace and speed for response (computed from segment distance + segment time)
                var prevDistance = raceCheckpoints.FirstOrDefault(c => c.Id == fromCheckpointId)?.DistanceFromStart ?? 0m;
                var segmentDistanceKm = editedCheckpoint.DistanceFromStart - prevDistance;
                decimal? paceMinPerKm = segmentDistanceKm > 0 && segmentTimeMs > 0
                    ? Math.Round(segmentTimeMs / 60000m / segmentDistanceKm, 4)
                    : null;
                decimal? speedKmh = segmentDistanceKm > 0 && segmentTimeMs > 0
                    ? Math.Round(segmentDistanceKm / (segmentTimeMs / 3600000m), 2)
                    : null;

                // Clamp for legacy TIME(7) column
                var splitTimeSpan = TimeSpan.FromMilliseconds(chipTimeMs);
                if (splitTimeSpan.TotalHours >= 24)
                    splitTimeSpan = new TimeSpan(23, 59, 59);

                // CHOSEN READ vs TYPED time. A chosen read is real hardware data the operator SELECTED:
                // the derived ReadNormalized keeps RawReadId = the chosen read and IsManualEntry = false
                // (so the read highlights as normalized, and no typed-"Manual" badge shows). A typed time
                // has no underlying read: RawReadId = null, IsManualEntry = true (legacy behaviour).
                var isChosenRead = chosenReadId.HasValue;
                var manualReason = isChosenRead ? "Manually selected read" : "Manual time entry";

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    // STEP A-1 — Upsert the DURABLE manual override. This is the authoritative INPUT
                    // that survives ClearProcessedData / reprocess (it lives in its own table, untouched
                    // by clear) and is re-applied onto ReadNormalized by Phase 2.4 on every rebuild. The
                    // ReadNormalized write in STEP A0 below is the live-display twin (so the grid reflects
                    // immediately); this override row is the source of truth for future reprocesses.
                    // Upsert the single ACTIVE row (filtered unique index: one active per participant+checkpoint).
                    var existingOverride = await overrideRepo.GetQuery(o =>
                        o.ParticipantId == decryptedParticipantId &&
                        o.CheckpointId == decryptedCheckpointId &&
                        !o.AuditProperties.IsDeleted)
                        .FirstOrDefaultAsync();

                    if (existingOverride != null)
                    {
                        existingOverride.EventId = decryptedEventId;
                        existingOverride.RaceId = decryptedRaceId;
                        existingOverride.ManualCrossingUtc = crossingUtc;
                        // Set on chosen-read, CLEAR on typed — a later typed edit on the same checkpoint
                        // flips this row back to a typed override (latest write wins on the single active row).
                        existingOverride.ChosenRawReadId = chosenReadId;
                        existingOverride.Reason = manualReason;
                        existingOverride.AuditProperties.IsActive = true;
                        existingOverride.AuditProperties.IsDeleted = false;
                        existingOverride.AuditProperties.UpdatedBy = userId;
                        existingOverride.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        await overrideRepo.UpdateAsync(existingOverride);
                    }
                    else
                    {
                        await overrideRepo.AddAsync(new ManualTimeOverride
                        {
                            EventId = decryptedEventId,
                            RaceId = decryptedRaceId,
                            ParticipantId = decryptedParticipantId,
                            CheckpointId = decryptedCheckpointId,
                            ManualCrossingUtc = crossingUtc,
                            ChosenRawReadId = chosenReadId,
                            Reason = manualReason,
                            CreatedByUserId = userId,
                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        });
                    }

                    // STEP A0 — Make this manual detection the SOLE normalized crossing at this
                    // (participant, checkpoint). Match on (participant, checkpoint) REGARDLESS of
                    // IsManualEntry: if an AUTOMATIC row exists it must be CONVERTED in place, not left
                    // beside a new manual row. The old query filtered rn.IsManualEntry, so it never saw
                    // the automatic row and AddAsync'd a second one — two crossings at one checkpoint,
                    // which is what fed the wrong-split bug. Invariant: exactly one active normalized
                    // crossing per checkpoint.
                    var rnsAtCheckpoint = await rnRepo.GetQuery(rn =>
                        rn.ParticipantId == decryptedParticipantId &&
                        rn.CheckpointId == decryptedCheckpointId &&
                        !rn.AuditProperties.IsDeleted)
                        .ToListAsync();

                    if (rnsAtCheckpoint.Count > 0)
                    {
                        var keep = rnsAtCheckpoint[0];
                        keep.ChipTime = crossingUtc;
                        keep.GunTime = chipTimeMs;
                        keep.NetTime = chipTimeMs;
                        keep.IsManualEntry = !isChosenRead;
                        keep.ManualEntryReason = manualReason;
                        // Chosen read keeps the link to its raw read (so it highlights as normalized);
                        // a typed time has no raw read.
                        keep.RawReadId = chosenReadId;
                        keep.CreatedByUserId = userId;
                        keep.AuditProperties.IsActive = true;
                        keep.AuditProperties.IsDeleted = false;
                        keep.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        keep.AuditProperties.UpdatedBy = userId;
                        await rnRepo.UpdateAsync(keep);

                        // Collapse any extra rows at this checkpoint (e.g. a pre-existing auto+manual pair)
                        // so exactly one survives.
                        foreach (var extra in rnsAtCheckpoint.Skip(1))
                        {
                            extra.AuditProperties.IsDeleted = true;
                            extra.AuditProperties.IsActive = false;
                            extra.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            extra.AuditProperties.UpdatedBy = userId;
                            await rnRepo.UpdateAsync(extra);
                        }
                    }
                    else
                    {
                        await rnRepo.AddAsync(new ReadNormalized
                        {
                            EventId = decryptedEventId,
                            ParticipantId = decryptedParticipantId,
                            CheckpointId = decryptedCheckpointId,
                            ChipTime = crossingUtc,
                            GunTime = chipTimeMs,
                            NetTime = chipTimeMs,
                            IsManualEntry = !isChosenRead,
                            ManualEntryReason = manualReason,
                            RawReadId = chosenReadId, // chosen-read keeps the link; typed = null
                            CreatedByUserId = userId,
                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        });
                    }

                    // STEP A — Update Results: always correct the status; only update times when editing finish
                    var existingResult = await resultsRepo.GetQuery(r =>
                        r.ParticipantId == decryptedParticipantId &&
                        r.EventId == decryptedEventId &&
                        r.RaceId == decryptedRaceId)
                        .FirstOrDefaultAsync();

                    if (existingResult != null)
                    {
                        if (isFinish)
                        {
                            existingResult.FinishTime = chipTimeMs;
                            existingResult.GunTime = chipTimeMs;
                            existingResult.NetTime = chipTimeMs;
                            existingResult.ManualFinishTimeMs = chipTimeMs;
                        }
                        existingResult.Status = computedStatus;
                        existingResult.IsManual = true;
                        existingResult.AuditProperties.IsActive = true;
                        existingResult.AuditProperties.IsDeleted = false;
                        existingResult.AuditProperties.UpdatedBy = userId;
                        existingResult.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        await resultsRepo.UpdateAsync(existingResult);
                    }
                    else if (isFinish)
                    {
                        await resultsRepo.AddAsync(new Results
                        {
                            EventId = decryptedEventId,
                            ParticipantId = decryptedParticipantId,
                            RaceId = decryptedRaceId,
                            FinishTime = chipTimeMs,
                            GunTime = chipTimeMs,
                            NetTime = chipTimeMs,
                            ManualFinishTimeMs = chipTimeMs,
                            Status = computedStatus,
                            IsManual = true,
                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        });
                    }

                    // STEP B — Mark participant as having manual timing
                    participant.IsManualTiming = true;
                    participant.AuditProperties.UpdatedBy = userId;
                    participant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    await participantRepo.UpdateAsync(participant);

                    // STEP C — Upsert SplitTimes row (create if first manual entry, update otherwise)
                    var existingSplit = await splitRepo.GetQuery(s =>
                        s.ParticipantId == decryptedParticipantId &&
                        s.CheckpointId == decryptedCheckpointId &&
                        !s.AuditProperties.IsDeleted)
                        .FirstOrDefaultAsync();

                    if (existingSplit != null)
                    {
                        existingSplit.SplitTimeMs = chipTimeMs;
                        existingSplit.SegmentTime = segmentTimeMs;
                        existingSplit.SplitTime = splitTimeSpan;
                        existingSplit.ReadNormalizedId = null;
                        existingSplit.IsManual = !isChosenRead; // chosen read = real hardware, no manual badge
                        existingSplit.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        existingSplit.AuditProperties.UpdatedBy = userId;
                        await splitRepo.UpdateAsync(existingSplit);
                    }
                    else
                    {
                        var newSplit = new SplitTimes
                        {
                            ParticipantId = decryptedParticipantId,
                            EventId = decryptedEventId,
                            FromCheckpointId = fromCheckpointId,
                            ToCheckpointId = decryptedCheckpointId,
                            CheckpointId = decryptedCheckpointId,
                            SplitTimeMs = chipTimeMs,
                            SegmentTime = segmentTimeMs,
                            SplitTime = splitTimeSpan,
                            IsManual = !isChosenRead, // chosen read = real hardware, no manual badge
                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };
                        await splitRepo.AddAsync(newSplit);
                    }

                    // STEP D — Recalculate the next checkpoint's SegmentTime
                    // The next segment starts at the checkpoint we just edited, so its delta changes
                    // NoTracking invariant (CONTEXT.md): the edited checkpoint's OWN split row can
                    // also match FromCheckpointId == decryptedCheckpointId — the start row is created
                    // with FromCheckpointId == CheckpointId (RFIDImportService.cs:4724), and any row
                    // satisfies CheckpointId == ToCheckpointId. Without excluding the edited checkpoint
                    // this query re-returns the row STEP C already loaded+attached (:1705) as a SECOND
                    // untracked instance with the same Id, and UpdateAsync below throws the duplicate-key
                    // tracking error. A genuine "next" segment ends at a LATER checkpoint, so its
                    // CheckpointId is never the edited one.
                    var nextSplit = await splitRepo.GetQuery(s =>
                        s.ParticipantId == decryptedParticipantId &&
                        s.FromCheckpointId == decryptedCheckpointId &&
                        s.CheckpointId != decryptedCheckpointId &&
                        !s.AuditProperties.IsDeleted)
                        .FirstOrDefaultAsync();

                    if (nextSplit?.SplitTimeMs.HasValue == true && nextSplit.Id != existingSplit?.Id)
                    {
                        nextSplit.SegmentTime = nextSplit.SplitTimeMs.Value - chipTimeMs;
                        nextSplit.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        nextSplit.AuditProperties.UpdatedBy = userId;
                        await splitRepo.UpdateAsync(nextSplit);
                    }

                    await _repository.SaveChangesAsync();

                    // Re-rank the WHOLE race unconditionally. A manual time can flip THIS runner
                    // Finished<->DNF, which shifts everyone they pass / are passed by — so re-ranking
                    // only when this entry is Finished would leave stale ranks on a demotion. The ranker
                    // re-ranks just the Finished set (overall/gender/category by finish time) via
                    // BulkUpdate (tracker-bypass), so it is correct and cheap whatever this entry's status.
                    await CalculateResultRankingsAsync(decryptedEventId, decryptedRaceId, userId);
                });

                int? overallRank = null, genderRank = null, categoryRank = null, totalFinishers = null;

                if (computedStatus == ResultStatus.Finished)
                {
                    // Fire-and-forget completion notification. MUST run on its OWN DI scope: the
                    // injected _raceNotificationService shares THIS request's scoped DbContext, so calling
                    // it on a background thread races the awaited reads below (updatedResult/count) on the
                    // same context ("A second operation was started on this context"). A fresh scope also
                    // avoids using the request context after it is disposed at end-of-request.
                    var notifyParticipantId = decryptedParticipantId;
                    var notifyRaceId = decryptedRaceId;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var notifier = scope.ServiceProvider.GetRequiredService<IRaceNotificationService>();
                            await notifier.NotifyRaceCompletionAsync(notifyParticipantId, notifyRaceId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Background race-completion notification failed for participant {ParticipantId}",
                                notifyParticipantId);
                        }
                    });

                    var updatedResult = await resultsRepo.GetQuery(r =>
                        r.ParticipantId == decryptedParticipantId &&
                        r.EventId == decryptedEventId &&
                        r.RaceId == decryptedRaceId &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();

                    var count = await resultsRepo.CountAsync(r =>
                        r.RaceId == decryptedRaceId &&
                        r.Status == ResultStatus.Finished &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted);

                    overallRank = updatedResult?.OverallRank;
                    genderRank = updatedResult?.GenderRank;
                    categoryRank = updatedResult?.CategoryRank;
                    totalFinishers = count;
                }

                _logger.LogInformation(
                    "Manual time recorded for participant {ParticipantId} at checkpoint {CheckpointId} ({CheckpointName}): {ChipTimeMs}ms",
                    decryptedParticipantId, decryptedCheckpointId, editedCheckpoint.Name, chipTimeMs);

                return new ManualTimeResponse
                {
                    ParticipantId = participantId,
                    Bib = participant.BibNumber ?? string.Empty,
                    FullName = participant.FullName,
                    CheckpointId = decryptedCheckpointId,
                    CheckpointName = editedCheckpoint.Name,
                    ChipTimeMs = chipTimeMs,
                    CumulativeTimeMs = chipTimeMs,
                    SplitTimeMs = segmentTimeMs,
                    Pace = paceMinPerKm,
                    Speed = speedKmh,
                    IsManual = true,
                    FinishTimeMs = isFinish ? chipTimeMs : null,
                    FinishTime = isFinish ? FormatTime(chipTimeMs) : null,
                    OverallRank = overallRank,
                    GenderRank = genderRank,
                    CategoryRank = categoryRank,
                    TotalFinishers = totalFinishers,
                    Status = computedStatus
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error recording manual time: {ex.Message}";
                _logger.LogError(ex, "Error recording manual time for participant {ParticipantId}", participantId);
                return null;
            }
        }

        public async Task<bool> RemoveManualTimeAsync(
            string eventId,
            string raceId,
            string participantId,
            string checkpointId,
            CancellationToken ct)
        {
            var userId = _userContext.UserId;
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
            var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));
            var decryptedCheckpointId = Convert.ToInt32(_encryptionService.Decrypt(checkpointId));

            try
            {
                var overrideRepo = _repository.GetRepository<ManualTimeOverride>();
                var existingOverride = await overrideRepo.GetQuery(o =>
                    o.ParticipantId == decryptedParticipantId &&
                    o.CheckpointId == decryptedCheckpointId &&
                    !o.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync(ct);

                if (existingOverride == null)
                {
                    ErrorMessage = "No manual override found for this checkpoint.";
                    return false;
                }

                // Race checkpoints for the per-DISTANCE mandatory-gate status recompute (mirrors
                // RecordManualTimeAsync / CalculateRaceResultsAsync).
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var raceCheckpoints = await checkpointRepo.GetQuery(c =>
                    c.RaceId == decryptedRaceId &&
                    c.AuditProperties.IsActive &&
                    !c.AuditProperties.IsDeleted)
                    .OrderBy(c => c.DistanceFromStart)
                    .AsNoTracking()
                    .ToListAsync(ct);

                var isFinishCheckpoint = raceCheckpoints.Count > 0
                    && raceCheckpoints[raceCheckpoints.Count - 1].Id == decryptedCheckpointId;

                var mandatoryDistances = raceCheckpoints.Any(c => c.IsMandatory)
                    ? raceCheckpoints.Where(c => c.IsMandatory).Select(c => c.DistanceFromStart).Distinct().ToList()
                    : new List<decimal> { raceCheckpoints.Max(c => c.DistanceFromStart) };

                var idsByMandatoryDistance = mandatoryDistances.ToDictionary(
                    d => d,
                    d => raceCheckpoints.Where(c => c.DistanceFromStart == d).Select(c => c.Id).ToHashSet());
                var mandatoryGateIds = idsByMandatoryDistance.Values.SelectMany(ids => ids).Distinct().ToList();

                var rnRepo = _repository.GetRepository<ReadNormalized>();
                var splitRepo = _repository.GetRepository<SplitTimes>();
                var resultsRepo = _repository.GetRepository<Results>();

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    // 1. Soft-delete the durable override. This releases the filtered unique slot
                    //    (WHERE IsDeleted=0), so a later re-override of the same checkpoint inserts cleanly.
                    existingOverride.AuditProperties.IsDeleted = true;
                    existingOverride.AuditProperties.IsActive = false;
                    existingOverride.AuditProperties.UpdatedBy = userId;
                    existingOverride.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    await overrideRepo.UpdateAsync(existingOverride);

                    // 2. Soft-delete the ReadNormalized row(s) at this checkpoint.
                    //    Key off (participant, checkpoint) — NOT IsManualEntry. A CHOSEN-READ override
                    //    writes IsManualEntry=false, so filtering on the flag would skip it and leave a
                    //    stale crossing after "revert" (the same lookup-by-flag class of bug as the old
                    //    STEP A0 duplicate). At an override checkpoint there is exactly one normalized row
                    //    (Phase 2 skip + STEP A0/Phase 2.4 collapse guarantee it), so deleting by key is
                    //    correct and flag-independent.
                    var manualRns = await rnRepo.GetQuery(rn =>
                        rn.ParticipantId == decryptedParticipantId &&
                        rn.CheckpointId == decryptedCheckpointId &&
                        !rn.AuditProperties.IsDeleted)
                        .ToListAsync();
                    foreach (var rn in manualRns)
                    {
                        rn.AuditProperties.IsDeleted = true;
                        rn.AuditProperties.IsActive = false;
                        rn.AuditProperties.UpdatedBy = userId;
                        rn.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    }
                    if (manualRns.Count > 0)
                        await rnRepo.UpdateRangeAsync(manualRns);

                    // 3. Soft-delete the SplitTimes row(s) at this checkpoint. Same reasoning as (2):
                    //    key off (participant, checkpoint), NOT s.IsManual — a chosen-read split is
                    //    IsManual=false and would otherwise survive the revert.
                    var manualSplits = await splitRepo.GetQuery(s =>
                        s.ParticipantId == decryptedParticipantId &&
                        s.CheckpointId == decryptedCheckpointId &&
                        !s.AuditProperties.IsDeleted)
                        .ToListAsync();
                    foreach (var s in manualSplits)
                    {
                        s.AuditProperties.IsDeleted = true;
                        s.AuditProperties.IsActive = false;
                        s.AuditProperties.UpdatedBy = userId;
                        s.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    }
                    if (manualSplits.Count > 0)
                        await splitRepo.UpdateRangeAsync(manualSplits);

                    await _repository.SaveChangesAsync();

                    // 4. Recompute status from the REMAINING (non-deleted) detections at the mandatory gates.
                    var remainingDetectedIds = await rnRepo.GetQuery(rn =>
                        rn.ParticipantId == decryptedParticipantId &&
                        mandatoryGateIds.Contains(rn.CheckpointId) &&
                        !rn.AuditProperties.IsDeleted)
                        .Select(rn => rn.CheckpointId)
                        .Distinct()
                        .ToListAsync();
                    var coveredCheckpointIds = remainingDetectedIds.ToHashSet();

                    var computedStatus = mandatoryDistances.All(d => idsByMandatoryDistance[d].Overlaps(coveredCheckpointIds))
                        ? ResultStatus.Finished
                        : ResultStatus.DNF;

                    var existingResult = await resultsRepo.GetQuery(r =>
                        r.ParticipantId == decryptedParticipantId &&
                        r.EventId == decryptedEventId &&
                        r.RaceId == decryptedRaceId)
                        .FirstOrDefaultAsync();

                    if (existingResult != null)
                    {
                        existingResult.Status = computedStatus;

                        if (isFinishCheckpoint)
                        {
                            // Finish reverted: pull the finish time from the remaining AUTO read at the
                            // finish checkpoint (if any) — else clear (truly empty → DNF). Next full
                            // reprocess re-derives everything else from raw.
                            var autoFinish = await rnRepo.GetQuery(rn =>
                                rn.ParticipantId == decryptedParticipantId &&
                                rn.CheckpointId == decryptedCheckpointId &&
                                !rn.AuditProperties.IsDeleted)
                                .OrderBy(rn => rn.ChipTime)
                                .FirstOrDefaultAsync();

                            if (autoFinish != null && computedStatus == ResultStatus.Finished)
                            {
                                existingResult.FinishTime = autoFinish.GunTime;
                                existingResult.GunTime = autoFinish.GunTime;
                                existingResult.NetTime = autoFinish.NetTime;
                                existingResult.ManualFinishTimeMs = null;
                            }
                            else
                            {
                                existingResult.FinishTime = null;
                                existingResult.GunTime = null;
                                existingResult.NetTime = null;
                                existingResult.ManualFinishTimeMs = null;
                            }
                        }
                        // Intermediate checkpoint revert: finish fields unchanged; status may still flip via gates.

                        // IsManual reflects whether ANY active override remains for this participant.
                        existingResult.IsManual = await overrideRepo.GetQuery(o =>
                            o.ParticipantId == decryptedParticipantId &&
                            !o.AuditProperties.IsDeleted)
                            .AnyAsync();
                        existingResult.AuditProperties.UpdatedBy = userId;
                        existingResult.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        await resultsRepo.UpdateAsync(existingResult);
                        await _repository.SaveChangesAsync();
                    }

                    // 5. Re-rank the whole race (cheap; BulkUpdate over the Finished set, tracker-bypass).
                    await CalculateResultRankingsAsync(decryptedEventId, decryptedRaceId, userId);
                });

                // Clear the participant's manual-timing flag if no active overrides remain.
                var hasRemainingOverrides = await overrideRepo.GetQuery(o =>
                    o.ParticipantId == decryptedParticipantId &&
                    !o.AuditProperties.IsDeleted)
                    .AnyAsync(ct);
                if (!hasRemainingOverrides)
                {
                    var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                    var participant = await participantRepo.GetQuery(p => p.Id == decryptedParticipantId)
                        .FirstOrDefaultAsync(ct);
                    if (participant != null && participant.IsManualTiming)
                    {
                        participant.IsManualTiming = false;
                        participant.AuditProperties.UpdatedBy = userId;
                        participant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        await participantRepo.UpdateAsync(participant);
                        await _repository.SaveChangesAsync();
                    }
                }

                _logger.LogInformation(
                    "Manual time override removed for participant {ParticipantId} at checkpoint {CheckpointId}",
                    decryptedParticipantId, decryptedCheckpointId);
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error removing manual time: {ex.Message}";
                _logger.LogError(ex, "Error removing manual time for participant {ParticipantId}", participantId);
                return false;
            }
        }

        public async Task<bool> ChangeParticipantCategoryAsync(
            string eventId,
            string raceId,
            string participantId,
            string newAgeCategory,
            CancellationToken ct)
        {
            try
            {
                var userId = _userContext.UserId;
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
                var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));

                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var participant = await participantRepo.GetQuery(p =>
                    p.Id == decryptedParticipantId &&
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync(ct);

                if (participant == null)
                {
                    ErrorMessage = "Participant not found";
                    return false;
                }

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    participant.AgeCategory = string.IsNullOrWhiteSpace(newAgeCategory) ? "Unknown" : newAgeCategory.Trim();
                    participant.AuditProperties.UpdatedBy = userId;
                    participant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    await participantRepo.UpdateAsync(participant);
                    await _repository.SaveChangesAsync();

                    await ReprocessParticipantInternalAsync(decryptedEventId, decryptedRaceId, decryptedParticipantId, userId);
                });

                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error changing participant category: {ex.Message}";
                _logger.LogError(ex, "Error in ChangeParticipantCategoryAsync for participant {ParticipantId}", participantId);
                return false;
            }
        }

        public async Task<bool> ProcessParticipantResultAsync(
            string eventId,
            string raceId,
            string participantId,
            CancellationToken ct)
        {
            try
            {
                var userId = _userContext.UserId;
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
                var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));

                var participantExists = await _repository.GetRepository<Models.Data.Entities.Participant>()
                    .GetQuery(p => p.Id == decryptedParticipantId
                        && p.RaceId == decryptedRaceId
                        && p.EventId == decryptedEventId
                        && p.AuditProperties.IsActive
                        && !p.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync(ct);

                if (!participantExists)
                {
                    ErrorMessage = "Participant not found";
                    return false;
                }

                // Rebuild this race's timing from raw via the proven pipeline (Phase 1 → 1.5 → 2 →
                // 2.5 → 3). This is what reconstructs a moved participant's results: the race-move
                // deleted their derived rows and reset their reads to Pending, and this pipeline
                // re-projects their retained RawRFIDReading crossings onto THIS race's checkpoints.
                // Idempotent for everyone else — Phase 1/2 skip-guards leave already-processed
                // (still-"Success", still-normalized) runners untouched, so only the participant
                // whose data was cleared is rebuilt. Runs on this request's fresh context
                // (process-result is a separate request from the edit/save that moved them), so it
                // does not collide with the move transaction under the global NoTracking default.
                var workflow = await _rfidImportService.ProcessCompleteWorkflowAsync(eventId, raceId);
                if (workflow.Status == "Failed")
                {
                    ErrorMessage = workflow.Errors.FirstOrDefault() ?? "Failed to reprocess race timing.";
                    return false;
                }

                // Confirm THIS participant's status from the freshly rebuilt ReadNormalized and
                // re-rank. Single fresh-queried entity → no NoTracking double-attach.
                await ReprocessParticipantInternalAsync(decryptedEventId, decryptedRaceId, decryptedParticipantId, userId);
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error processing participant result: {ex.Message}";
                _logger.LogError(ex, "Error in ProcessParticipantResultAsync for participant {ParticipantId}", participantId);
                return false;
            }
        }

        private async Task ReprocessParticipantInternalAsync(int eventId, int raceId, int participantId, int userId)
        {
            var resultsRepo = _repository.GetRepository<Results>();
            var result = await resultsRepo.GetQuery(r =>
                r.ParticipantId == participantId &&
                r.EventId == eventId &&
                r.RaceId == raceId &&
                !r.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync();

            if (result != null)
            {
                result.Status = await ComputeParticipantStatusAsync(eventId, raceId, participantId);
                result.AuditProperties.UpdatedBy = userId;
                result.AuditProperties.UpdatedDate = DateTime.UtcNow;
                await resultsRepo.UpdateAsync(result);
                await _repository.SaveChangesAsync();
            }

            await CalculateResultRankingsAsync(eventId, raceId, userId);
        }

        private async Task<string> ComputeParticipantStatusAsync(int eventId, int raceId, int participantId)
        {
            // BUG-26: mandatory evaluation is per-DISTANCE, not per-checkpoint-ID. Two devices at
            // the same DistanceFromStart are ONE logical gate — a detection at ANY checkpoint at
            // that distance satisfies it. Mirrors RFIDImportService.CalculateRaceResultsAsync.
            var allCheckpoints = await _repository.GetRepository<Checkpoint>()
                .GetQuery(cp => cp.RaceId == raceId
                             && cp.EventId == eventId
                             && cp.AuditProperties.IsActive
                             && !cp.AuditProperties.IsDeleted)
                .Select(cp => new { cp.Id, cp.DistanceFromStart, cp.IsMandatory })
                .ToListAsync();

            if (allCheckpoints.Count == 0)
                return ResultStatus.DNF;

            var mandatoryDistances = allCheckpoints.Any(cp => cp.IsMandatory)
                ? allCheckpoints.Where(cp => cp.IsMandatory).Select(cp => cp.DistanceFromStart).Distinct().ToList()
                // Fallback: the highest distance acts as the mandatory finish gate
                : new List<decimal> { allCheckpoints.Max(cp => cp.DistanceFromStart) };

            var idsByMandatoryDistance = mandatoryDistances.ToDictionary(
                d => d,
                d => allCheckpoints.Where(cp => cp.DistanceFromStart == d).Select(cp => cp.Id).ToHashSet());

            var gateIds = idsByMandatoryDistance.Values.SelectMany(ids => ids).Distinct().ToList();

            var detectedIds = (await _repository.GetRepository<ReadNormalized>()
                .GetQuery(rn => rn.ParticipantId == participantId
                             && gateIds.Contains(rn.CheckpointId)
                             && !rn.AuditProperties.IsDeleted)
                .Select(rn => rn.CheckpointId)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

            return mandatoryDistances.All(d => idsByMandatoryDistance[d].Overlaps(detectedIds))
                ? ResultStatus.Finished
                : ResultStatus.DNF;
        }
    }
}
