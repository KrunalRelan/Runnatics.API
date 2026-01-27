using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Results;
using Runnatics.Models.Client.Responses.Results;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

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
                var splitTimeRepo = _repository.GetRepository<SplitTime>();

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

                var splitTimes = new List<SplitTime>();
                var checkpointSummaries = new Dictionary<int, CheckpointSummary>();

                await _repository.BeginTransactionAsync();

                try
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

                            var splitTime = new SplitTime
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
                        await _repository.SaveChangesAsync();

                        // Calculate rankings at each checkpoint
                        await CalculateSplitTimeRankingsAsync(decryptedEventId, decryptedRaceId, userId);
                    }

                    await _repository.CommitTransactionAsync();

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
                    await _repository.RollbackTransactionAsync();
                    throw;
                }
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
                var splitTimeRepo = _repository.GetRepository<SplitTime>();
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

                await _repository.BeginTransactionAsync();

                try
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
                            NetTime = split.SplitTimeMs, // TODO: Calculate actual net time
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
                        await _repository.SaveChangesAsync();

                        // Calculate rankings
                        await CalculateResultRankingsAsync(decryptedEventId, decryptedRaceId, userId);
                    }

                    await _repository.CommitTransactionAsync();

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
                    await _repository.RollbackTransactionAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error calculating results: {ex.Message}";
                _logger.LogError(ex, "Error calculating results");
                response.Status = "Failed";
                return response;
            }
        }

        public async Task<LeaderboardResponse> GetLeaderboardAsync(
            string eventId,
            string raceId,
            string rankBy = "overall",
            string? gender = null,
            string? category = null,
            int page = 1,
            int pageSize = 50,
            bool includeSplits = false)
        {
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
            var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

            try
            {
                var resultsRepo = _repository.GetRepository<Results>();
                IQueryable<Results> query = resultsRepo.GetQuery(r =>
                    r.EventId == decryptedEventId &&
                    r.RaceId == decryptedRaceId &&
                    r.Status == "Finished" &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .Include(r => r.Participant);

                // Apply filters
                if (!string.IsNullOrEmpty(gender))
                {
                    query = query.Where(r => r.Participant.Gender == gender);
                }

                if (!string.IsNullOrEmpty(category))
                {
                    query = query.Where(r => r.Participant.AgeCategory == category);
                }

                // Order by rank type
                query = rankBy.ToLower() switch
                {
                    "gender" => query.OrderBy(r => r.GenderRank),
                    "category" => query.OrderBy(r => r.CategoryRank),
                    _ => query.OrderBy(r => r.OverallRank)
                };

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var results = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var leaderboardEntries = new List<LeaderboardEntry>();

                foreach (var result in results)
                {
                    var entry = new LeaderboardEntry
                    {
                        Rank = rankBy.ToLower() switch
                        {
                            "gender" => result.GenderRank ?? 0,
                            "category" => result.CategoryRank ?? 0,
                            _ => result.OverallRank ?? 0
                        },
                        ParticipantId = _encryptionService.Encrypt(result.ParticipantId.ToString()),
                        Bib = result.Participant.BibNumber ?? string.Empty,
                        FirstName = result.Participant.FirstName ?? string.Empty,
                        LastName = result.Participant.LastName ?? string.Empty,
                        Gender = result.Participant.Gender ?? string.Empty,
                        Category = result.Participant.AgeCategory,
                        Age = result.Participant.Age,
                        City = result.Participant.City ?? string.Empty,
                        FinishTimeMs = result.FinishTime,
                        GunTimeMs = result.GunTime,
                        NetTimeMs = result.NetTime,
                        FinishTime = result.FinishTime.HasValue ? FormatTime(result.FinishTime.Value) : null,
                        GunTime = result.GunTime.HasValue ? FormatTime(result.GunTime.Value) : null,
                        NetTime = result.NetTime.HasValue ? FormatTime(result.NetTime.Value) : null,
                        OverallRank = result.OverallRank,
                        GenderRank = result.GenderRank,
                        CategoryRank = result.CategoryRank,
                        Status = result.Status
                    };

                    // Calculate average pace if we have finish time and race distance
                    var raceRepo = _repository.GetRepository<Race>();
                    var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId).FirstOrDefaultAsync();
                    if (race != null && result.FinishTime.HasValue && race.Distance > 0)
                    {
                        var timeInMinutes = result.FinishTime.Value / 60000.0m;
                        entry.AveragePace = timeInMinutes / race.Distance;
                        entry.AveragePaceFormatted = FormatPace(entry.AveragePace.Value);
                    }

                    // Include splits if requested
                    if (includeSplits)
                    {
                        entry.Splits = await GetParticipantSplitsAsync(result.ParticipantId, decryptedEventId);
                    }

                    leaderboardEntries.Add(entry);
                }

                return new LeaderboardResponse
                {
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = totalPages,
                    RankBy = rankBy,
                    Gender = gender,
                    Category = category,
                    Results = leaderboardEntries
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

        #region Private Helper Methods

        private async Task<List<SplitTimeInfo>> GetParticipantSplitsAsync(int participantId, int eventId)
        {
            var splitTimeRepo = _repository.GetRepository<SplitTime>();
            var splits = await splitTimeRepo.GetQuery(st =>
                st.ParticipantId == participantId &&
                st.EventId == eventId &&
                st.AuditProperties.IsActive &&
                !st.AuditProperties.IsDeleted)
                .Include(st => st.Checkpoint)
                .OrderBy(st => st.Checkpoint.DistanceFromStart)
                .ToListAsync();

            return splits.Select(s => new SplitTimeInfo
            {
                CheckpointId = _encryptionService.Encrypt(s.CheckpointId.ToString()),
                CheckpointName = s.Checkpoint.Name ?? $"CP{s.Checkpoint.DistanceFromStart}km",
                DistanceKm = s.Checkpoint.DistanceFromStart,
                SplitTimeMs = s.SplitTimeMs,
                SegmentTimeMs = s.SegmentTime,
                SplitTime = FormatTime(s.SplitTimeMs),
                SegmentTime = s.SegmentTime.HasValue ? FormatTime(s.SegmentTime.Value) : null,
                Pace = s.Pace,
                PaceFormatted = s.Pace.HasValue ? FormatPace(s.Pace.Value) : null,
                Rank = s.Rank,
                GenderRank = s.GenderRank,
                CategoryRank = s.CategoryRank
            }).ToList();
        }

        private async Task CalculateSplitTimeRankingsAsync(int eventId, int raceId, int userId)
        {
            var splitTimeRepo = _repository.GetRepository<SplitTime>();
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
    }
}
