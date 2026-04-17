using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Client.Responses.Results;
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

        public ResultsService(
            IUnitOfWork<RaceSyncDbContext> repository,
            IMapper mapper,
            ILogger<ResultsService> logger,
            IUserContextService userContext,
            IEncryptionService encryptionService)
            : base(repository)
        {
            _mapper = mapper;
            _logger = logger;
            _userContext = userContext;
            _encryptionService = encryptionService;
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

                // Check if leaderboard is allowed
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

                // Fetch race once for pace calculation
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId).FirstOrDefaultAsync();

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

        /// <summary>
        /// Loads checkpoint times from ReadNormalized readings and dynamically calculates
        /// per-checkpoint rankings by comparing this participant's times against all other
        /// participants in the race.
        /// </summary>
        private async Task<List<CheckpointTimeInfo>> LoadCheckpointTimesAsync(
            int participantId, int eventId, int raceId, string? eventTimeZone)
        {
            var checkpointTimeInfos = new List<CheckpointTimeInfo>();

            // Get all checkpoints for this race ordered by distance
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

            // Get ALL normalized readings for this race's participants to calculate rankings
            var normalizedRepo = _repository.GetRepository<ReadNormalized>();
            var allReadings = await normalizedRepo.GetQuery(r =>
                r.EventId == eventId &&
                r.Participant.RaceId == raceId &&
                r.AuditProperties.IsActive &&
                !r.AuditProperties.IsDeleted)
                .Include(r => r.Participant)
                .AsNoTracking()
                .ToListAsync();

            // Group by checkpoint → per participant keep earliest reading, sorted by GunTime
            var rankedByCheckpoint = allReadings
                .GroupBy(r => r.CheckpointId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(r => r.ParticipantId)
                          .Select(pg => pg.OrderBy(r => r.ChipTime).First())
                          .OrderBy(r => r.GunTime ?? long.MaxValue)
                          .ToList());

            // Get current participant's gender and category for ranking
            var currentParticipant = allReadings
                .FirstOrDefault(r => r.ParticipantId == participantId)?.Participant;

            // Resolve timezone
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
                        // Set checkpoint time
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(participantReading.ChipTime, timeZone);
                        info.Time = localTime.ToString("HH:mm:ss");

                        // Overall rank: position among all participants at this checkpoint
                        info.OverallRank = sortedReadings
                            .Select((r, idx) => new { r.ParticipantId, Rank = idx + 1 })
                            .First(x => x.ParticipantId == participantId).Rank;

                        // Gender rank: position among same-gender participants
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

                        // Category rank: position among same-category participants
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

        /// <summary>
        /// Loads RFID readings from ReadNormalized table for a participant.
        /// Includes checkpoint name from Checkpoint table and converts ChipTime from UTC
        /// to the event's local timezone using the Event.TimeZone column.
        /// </summary>
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

            // Resolve timezone from event (IANA id like "Asia/Kolkata")
            TimeZoneInfo timeZone;
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZone ?? "India Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }

            // Set computed fields
            for (int i = 0; i < readings.Count; i++)
            {
                // Convert ChipTime (UTC) to event local time
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(readings[i].ChipTime, timeZone);
                result[i].ReadTimeLocal = localTime.ToString("HH:mm:ss");

                // Format gun time and net time
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

        private static LeaderboardDisplaySettings BuildDisplaySettings(
            EventSettings? eventSettings,
            RaceSettings? raceSettings,
            LeaderboardSettings? leaderboardSettings,
            string rankBy)
        {
            var display = new LeaderboardDisplaySettings();

            // Event-level settings
            if (eventSettings != null)
            {
                display.RankOnNet = eventSettings.RankOnNet;
            }

            // Race-level settings
            if (raceSettings != null)
            {
                display.ShowDnf = raceSettings.PublishDnf;
            }

            // Leaderboard-level settings
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

                // Determine sort time field based on current view
                display.SortTimeField = rankBy switch
                {
                    "category" => leaderboardSettings.SortByCategoryChipTime == true ? "NetTime" : "GunTime",
                    _ => leaderboardSettings.SortByOverallChipTime == true ? "NetTime" : "GunTime"
                };
            }

            // RankOnNet from event settings overrides sort field
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

            // Format times and pace
            for (int i = 0; i < splits.Count; i++)
            {
                splitInfos[i].SplitTime = FormatTime(splits[i].SplitTimeMs ?? 0);
                splitInfos[i].SegmentTime = splits[i].SegmentTime.HasValue ? FormatTime(splits[i].SegmentTime.Value) : null;
                splitInfos[i].PaceFormatted = splits[i].Pace.HasValue ? FormatPace(splits[i].Pace.Value) : null;
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

                // Overall ranking
                var rank = 1;
                foreach (var split in splits)
                {
                    split.Rank = rank++;
                    split.AuditProperties.UpdatedBy = userId;
                    split.AuditProperties.UpdatedDate = DateTime.UtcNow;
                }

                // Gender ranking
                foreach (var gender in new[] { "Male", "Female", "Others" })
                {
                    var genderSplits = splits.Where(s => s.Participant.Gender == gender).ToList();
                    rank = 1;
                    foreach (var split in genderSplits)
                    {
                        split.GenderRank = rank++;
                    }
                }

                // Category ranking
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
                .OrderBy(r => r.FinishTime)
                .ToListAsync();

            // Overall ranking
            var rank = 1;
            foreach (var result in results)
            {
                result.OverallRank = rank++;
                result.AuditProperties.UpdatedBy = userId;
                result.AuditProperties.UpdatedDate = DateTime.UtcNow;
            }

            // Gender ranking
            foreach (var gender in new[] { "Male", "Female", "Others" })
            {
                var genderResults = results.Where(r => r.Participant.Gender == gender).ToList();
                rank = 1;
                foreach (var result in genderResults)
                {
                    result.GenderRank = rank++;
                }
            }

            // Category ranking
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

            await resultsRepo.UpdateRangeAsync(results);
            await _repository.SaveChangesAsync();
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

        #region Public (no-auth) methods

        public async Task<Models.Data.Common.PagingList<Results>> GetPublicResultsAsync(
            int eventId,
            string? raceName,
            string? searchQuery,
            string? gender,
            int page,
            int pageSize)
        {
            try
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 100);

                var resultsRepo = _repository.GetRepository<Results>();

                var query = resultsRepo.GetQuery(r =>
                    r.EventId == eventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .Include(r => r.Participant)
                    .Include(r => r.Race)
                    .Include(r => r.Participant.SplitTimes
                        .Where(st => st.EventId == eventId &&
                                     st.AuditProperties.IsActive &&
                                     !st.AuditProperties.IsDeleted))
                        .ThenInclude(st => st.ToCheckpoint)
                    .AsNoTracking();

                if (!string.IsNullOrWhiteSpace(raceName))
                    query = query.Where(r => r.Race.Title.Contains(raceName));

                if (!string.IsNullOrWhiteSpace(searchQuery))
                    query = query.Where(r =>
                        (r.Participant.BibNumber != null && r.Participant.BibNumber.Contains(searchQuery)) ||
                        (r.Participant.FirstName != null && r.Participant.FirstName.Contains(searchQuery)) ||
                        (r.Participant.LastName  != null && r.Participant.LastName.Contains(searchQuery)));

                if (!string.IsNullOrWhiteSpace(gender))
                    query = query.Where(r => r.Participant.Gender != null &&
                                             r.Participant.Gender.ToLower() == gender.ToLower());

                var totalCount = await query.CountAsync();

                var items = await query
                    .OrderBy(r => r.OverallRank)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var result = new Models.Data.Common.PagingList<Results>();
                result.AddRange(items);
                result.TotalCount = totalCount;

                _logger.LogInformation(
                    "Public results for event {EventId}: returned {Count}/{Total}.",
                    eventId, items.Count, totalCount);

                return result;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "Error retrieving public results.";
                _logger.LogError(ex, "Error in GetPublicResultsAsync for event {EventId}", eventId);
                return [];
            }
        }

        #endregion
    }
}
