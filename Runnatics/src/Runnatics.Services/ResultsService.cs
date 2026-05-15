using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Client.Responses.Results;
using Runnatics.Models.Client.Responses.RFID;
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

        private readonly IRaceNotificationService _raceNotificationService;

        public ResultsService(
            IUnitOfWork<RaceSyncDbContext> repository,
            IMapper mapper,
            ILogger<ResultsService> logger,
            IUserContextService userContext,
            IEncryptionService encryptionService,
            IRaceNotificationService raceNotificationService)
            : base(repository)
        {
            _mapper = mapper;
            _logger = logger;
            _userContext = userContext;
            _encryptionService = encryptionService;
            _raceNotificationService = raceNotificationService;
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
                        var participantHasSplits = false;

                        foreach (var reading in participantData.Readings)
                        {
                            var checkpoint = checkpoints.FirstOrDefault(c => c.Id == reading.CheckpointId);
                            if (checkpoint == null) continue;

                            var splitTimeMs = reading.GunTime ?? 0;
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
                                CheckpointId = reading.CheckpointId,
                                ReadNormalizedId = reading.Id,
                                SplitTimeMs = splitTimeMs,
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

                // Get finish checkpoint (highest distance)
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var finishCheckpoint = await checkpointRepo.GetQuery(c =>
                    c.RaceId == decryptedRaceId &&
                    c.EventId == decryptedEventId &&
                    c.AuditProperties.IsActive &&
                    !c.AuditProperties.IsDeleted)
                    .OrderByDescending(c => c.DistanceFromStart)
                    .FirstOrDefaultAsync();

                if (finishCheckpoint == null)
                {
                    ErrorMessage = "No checkpoints found for this race";
                    response.Status = "Failed";
                    return response;
                }

                // Get all participants in the race
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var allParticipants = await participantRepo.GetQuery(p =>
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .ToListAsync();

                response.TotalParticipants = allParticipants.Count;

                // Get split times at finish checkpoint
                var splitTimeRepo = _repository.GetRepository<SplitTimes>();
                var finishSplits = await splitTimeRepo.GetQuery(st =>
                    st.EventId == decryptedEventId &&
                    st.CheckpointId == finishCheckpoint.Id &&
                    st.Participant.RaceId == decryptedRaceId &&
                    st.AuditProperties.IsActive &&
                    !st.AuditProperties.IsDeleted)
                    .Include(st => st.Participant)
                    .ToListAsync();

                var finishers = finishSplits.Select(fs => fs.ParticipantId).ToHashSet();
                response.Finishers = finishers.Count;
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
                    // Create results for finishers
                    foreach (var split in finishSplits.OrderBy(fs => fs.SplitTimeMs))
                    {
                        var result = new Results
                        {
                            EventId = decryptedEventId,
                            ParticipantId = split.ParticipantId,
                            RaceId = decryptedRaceId,
                            FinishTime = split.SplitTimeMs,
                            GunTime = split.SplitTimeMs,
                            NetTime = split.SplitTimeMs,
                            Status = "Finished",
                            IsOfficial = request.MarkAsOfficial,
                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };

                        results.Add(result);
                    }

                    // Create DNF results
                    var dnfParticipants = allParticipants.Where(p => !finishers.Contains(p.Id));
                    foreach (var participant in dnfParticipants)
                    {
                        var result = new Results
                        {
                            EventId = decryptedEventId,
                            ParticipantId = participant.Id,
                            RaceId = decryptedRaceId,
                            Status = "DNF",
                            IsOfficial = request.MarkAsOfficial,
                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };

                        results.Add(result);
                    }

                    // Bulk insert results
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
            var checkpoints = await checkpointRepo.GetQuery(c =>
                c.RaceId == raceId &&
                c.EventId == eventId &&
                c.AuditProperties.IsActive &&
                !c.AuditProperties.IsDeleted)
                .OrderBy(c => c.DistanceFromStart)
                .AsNoTracking()
                .ToListAsync();

            if (checkpoints.Count == 0)
                return checkpointTimeInfos;

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
                    CheckpointName = checkpoint.Name ?? $"CP {checkpoint.DistanceFromStart}",
                    DistanceKm = checkpoint.DistanceFromStart
                };

                if (rankedByCheckpoint.TryGetValue(checkpoint.Id, out var sortedReadings))
                {
                    var participantReading = sortedReadings
                        .FirstOrDefault(r => r.ParticipantId == participantId);

                    if (participantReading != null)
                    {
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(participantReading.ChipTime, timeZone);
                        info.Time = localTime.ToString("HH:mm:ss");

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

                foreach (var gender in new[] { "Male", "Female", "Others" })
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
                r.Status == "Finished" &&
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

            foreach (var gender in new[] { "Male", "Female", "Others" })
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
            long finishTimeMs,
            string checkpointId)
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

                // finishTimeMs = elapsed chip time in ms from race gun start
                // For backwards compatibility with IST-based clients, we accept either:
                //   - elapsed ms from race start (when finishTimeMs < 24h = 86_400_000)
                //   - ms-from-midnight in IST (legacy; detected when close to wall-clock time)
                var raceStartUtc = race.StartTime.Value;
                long chipTimeMs;

                if (finishTimeMs > 0 && finishTimeMs < 86_400_000)
                {
                    // Treat as elapsed ms from race start (new convention)
                    chipTimeMs = finishTimeMs;
                }
                else
                {
                    // Legacy: ms-from-midnight in IST
                    var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
                    var raceStartIst = TimeZoneInfo.ConvertTimeFromUtc(raceStartUtc, istZone);
                    var finishIst = raceStartIst.Date.AddMilliseconds(finishTimeMs);
                    var finishUtc = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(finishIst, DateTimeKind.Unspecified), istZone);
                    chipTimeMs = (long)(finishUtc - raceStartUtc).TotalMilliseconds;
                }

                if (chipTimeMs <= 0 || chipTimeMs > 86_400_000)
                {
                    ErrorMessage = $"Calculated chip time {chipTimeMs}ms is invalid. Check that finish time is after race start.";
                    return null;
                }

                var resultsRepo = _repository.GetRepository<Results>();
                var splitRepo = _repository.GetRepository<SplitTimes>();

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

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    // STEP A — Update Results only when editing the finish checkpoint
                    if (isFinish)
                    {
                        var existing = await resultsRepo.GetQuery(r =>
                            r.ParticipantId == decryptedParticipantId &&
                            r.EventId == decryptedEventId &&
                            r.RaceId == decryptedRaceId)
                            .FirstOrDefaultAsync();

                        if (existing != null)
                        {
                            existing.FinishTime = chipTimeMs;
                            existing.GunTime = chipTimeMs;
                            existing.NetTime = chipTimeMs;
                            existing.ManualFinishTimeMs = chipTimeMs;
                            existing.Status = "Finished";
                            existing.IsManual = true;
                            existing.AuditProperties.IsActive = true;
                            existing.AuditProperties.IsDeleted = false;
                            existing.AuditProperties.UpdatedBy = userId;
                            existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            await resultsRepo.UpdateAsync(existing);
                        }
                        else
                        {
                            var newResult = new Results
                            {
                                EventId = decryptedEventId,
                                ParticipantId = decryptedParticipantId,
                                RaceId = decryptedRaceId,
                                FinishTime = chipTimeMs,
                                GunTime = chipTimeMs,
                                NetTime = chipTimeMs,
                                ManualFinishTimeMs = chipTimeMs,
                                Status = "Finished",
                                IsManual = true,
                                AuditProperties = new Models.Data.Common.AuditProperties
                                {
                                    CreatedBy = userId,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                }
                            };
                            await resultsRepo.AddAsync(newResult);
                        }
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
                        existingSplit.IsManual = true;
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
                            IsManual = true,
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
                    var nextSplit = await splitRepo.GetQuery(s =>
                        s.ParticipantId == decryptedParticipantId &&
                        s.FromCheckpointId == decryptedCheckpointId &&
                        !s.AuditProperties.IsDeleted)
                        .FirstOrDefaultAsync();

                    if (nextSplit?.SplitTimeMs.HasValue == true)
                    {
                        nextSplit.SegmentTime = nextSplit.SplitTimeMs.Value - chipTimeMs;
                        nextSplit.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        nextSplit.AuditProperties.UpdatedBy = userId;
                        await splitRepo.UpdateAsync(nextSplit);
                    }

                    await _repository.SaveChangesAsync();

                    if (isFinish)
                        await CalculateResultRankingsAsync(decryptedEventId, decryptedRaceId, userId);
                });

                int? overallRank = null, genderRank = null, categoryRank = null, totalFinishers = null;
                string? status = null;

                if (isFinish)
                {
                    _ = Task.Run(() => _raceNotificationService.NotifyRaceCompletionAsync(
                        decryptedParticipantId, decryptedRaceId));

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
                        r.Status == "Finished" &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted);

                    overallRank = updatedResult?.OverallRank;
                    genderRank = updatedResult?.GenderRank;
                    categoryRank = updatedResult?.CategoryRank;
                    totalFinishers = count;
                    status = "Finished";
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
                    Status = status
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error recording manual time: {ex.Message}";
                _logger.LogError(ex, "Error recording manual time for participant {ParticipantId}", participantId);
                return null;
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
                result.AuditProperties.UpdatedBy = userId;
                result.AuditProperties.UpdatedDate = DateTime.UtcNow;
                await resultsRepo.UpdateAsync(result);
                await _repository.SaveChangesAsync();
            }

            await CalculateResultRankingsAsync(eventId, raceId, userId);
        }
    }
}
