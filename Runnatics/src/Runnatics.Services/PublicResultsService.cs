using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Public;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using DataResultsPagingList = Runnatics.Models.Data.Common.PagingList<Runnatics.Models.Data.Entities.Results>;

namespace Runnatics.Services
{
    public class PublicResultsService : ServiceBase<IUnitOfWork<RaceSyncDbContext>>, IPublicResultsService
    {
        private readonly ILogger<PublicResultsService> _logger;
        private readonly IEncryptionService _encryptionService;

        public PublicResultsService(
            IUnitOfWork<RaceSyncDbContext> repository,
            ILogger<PublicResultsService> logger,
            IEncryptionService encryptionService)
            : base(repository)
        {
            _logger = logger;
            _encryptionService = encryptionService;
        }

        public async Task<PublicResultsResponseDto?> GetPublicEventResultsAsync(
            string slug, string? race, string? q, string? gender,
            int page, int pageSize, CancellationToken ct = default)
        {
            try
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 100);

                var eventRepo = _repository.GetRepository<Event>();
                var eventEntity = await eventRepo.GetQuery(e =>
                    e.Slug == slug &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted &&
                    e.EventSettings != null &&
                    e.EventSettings.ConfirmedEvent &&
                    !e.EventSettings.Published)
                    .Include(e => e.EventSettings)
                    .Include(e => e.Races.Where(r => r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted))
                        .ThenInclude(r => r.RaceSettings)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (eventEntity == null)
                    return null;

                var isEventPublished = eventEntity.EventSettings?.Published ?? false;
                var publishedRaces = eventEntity.Races?
                    .Where(r => r.RaceSettings == null || r.RaceSettings.Published)
                    .ToList() ?? [];

                if (!isEventPublished)
                    return new PublicResultsResponseDto
                    {
                        IsPublished = false,
                        StatusMessage = "Results not yet published for this event.",
                        Results = [],
                        Races = [],
                        LeaderboardSettings = new PublicLeaderboardSettingsDto()
                    };

                var anyShowResultTable = publishedRaces.Any(r => r.RaceSettings == null || r.RaceSettings.ShowResultTable);
                if (!anyShowResultTable)
                    return new PublicResultsResponseDto
                    {
                        IsPublished = true,
                        StatusMessage = "Results not available for this event.",
                        Results = [],
                        Races = publishedRaces.Select(r => r.Title).ToList(),
                        LeaderboardSettings = new PublicLeaderboardSettingsDto()
                    };

                var selectedRace = !string.IsNullOrEmpty(race)
                    ? publishedRaces.FirstOrDefault(r => r.Title.Equals(race, StringComparison.OrdinalIgnoreCase))
                    : null;

                var leaderboardSettings = await GetEffectivePublicLeaderboardSettingsAsync(eventEntity.Id, selectedRace?.Id);

                var results = await GetPublicResultsAsync(eventEntity.Id, race, q, gender, page, pageSize);

                var raceSettingsMap = publishedRaces
                    .Where(r => r.RaceSettings != null)
                    .ToDictionary(r => r.Id, r => r.RaceSettings!);

                var filteredResults = results.Where(r =>
                {
                    if (!publishedRaces.Any(pr => pr.Id == r.RaceId))
                        return false;
                    if (raceSettingsMap.TryGetValue(r.RaceId, out var rs) && !rs.PublishDnf && r.Status == "DNF")
                        return false;
                    return true;
                }).ToList();

                return new PublicResultsResponseDto
                {
                    Results = filteredResults.Select(MapToResultDto).ToList(),
                    Races = publishedRaces.Select(r => r.Title).ToList(),
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = results.TotalCount,
                    LeaderboardSettings = leaderboardSettings,
                    IsPublished = true
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error retrieving event results.";
                _logger.LogError(ex, "Error in GetPublicEventResultsAsync for slug {Slug}", slug);
                return null;
            }
        }

        public async Task<PublicResultDto?> GetPublicResultByBibAsync(
            string slug, string bib, CancellationToken ct = default)
        {
            try
            {
                var eventRepo = _repository.GetRepository<Event>();
                var eventEntity = await eventRepo.GetQuery(e =>
                    e.Slug == slug &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted &&
                    e.EventSettings != null &&
                    e.EventSettings.ConfirmedEvent &&
                    !e.EventSettings.Published)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (eventEntity == null)
                    return null;

                var results = await GetPublicResultsAsync(
                    eventEntity.Id, raceName: null, searchQuery: bib, gender: null, page: 1, pageSize: 10);

                var match = results.FirstOrDefault(r =>
                    r.Participant?.BibNumber != null &&
                    r.Participant.BibNumber.Equals(bib, StringComparison.OrdinalIgnoreCase));

                return match == null ? null : MapToResultDto(match);
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error retrieving result by bib.";
                _logger.LogError(ex, "Error in GetPublicResultByBibAsync for slug {Slug}, bib {Bib}", slug, bib);
                return null;
            }
        }

        private async Task<DataResultsPagingList> GetPublicResultsAsync(
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
                    r.Event.EventSettings != null &&
                    r.Event.EventSettings.Published &&
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
                ErrorMessage = "Error retrieving public results.";
                _logger.LogError(ex, "Error in GetPublicResultsAsync for event {EventId}", eventId);
                return [];
            }
        }

        private static PublicResultDto MapToResultDto(Results r) => new()
        {
            BibNumber = r.Participant?.BibNumber ?? string.Empty,
            ParticipantName = r.Participant?.FullName ?? string.Empty,
            RaceName = r.Race?.Title ?? string.Empty,
            AgeGroup = r.Participant?.AgeCategory,
            Gender = r.Participant?.Gender,
            GunTime = r.GunTime.HasValue ? TimeSpan.FromMilliseconds(r.GunTime.Value) : null,
            NetTime = r.NetTime.HasValue ? TimeSpan.FromMilliseconds(r.NetTime.Value) : null,
            OverallRank = r.OverallRank,
            CategoryRank = r.CategoryRank,
            GenderRank = r.GenderRank,
            Splits = r.Participant?.SplitTimes?
                .OrderBy(st => st.ToCheckpoint?.DistanceFromStart)
                .Select(st => new PublicSplitDto
                {
                    CheckpointName = st.ToCheckpoint?.Name ?? string.Empty,
                    Time = st.SplitTimeMs.HasValue
                        ? TimeSpan.FromMilliseconds(st.SplitTimeMs.Value)
                        : st.SplitTime,
                    Rank = st.Rank
                })
                .ToList()
        };

        private async Task<PublicLeaderboardSettingsDto> GetEffectivePublicLeaderboardSettingsAsync(
            int eventId, int? raceId)
        {
            try
            {
                var repo = _repository.GetRepository<LeaderboardSettings>();

                if (raceId.HasValue)
                {
                    var raceSettings = await repo
                        .GetQuery(s =>
                            s.EventId == eventId &&
                            s.RaceId == raceId &&
                            s.OverrideSettings == true &&
                            s.AuditProperties.IsActive &&
                            !s.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();

                    if (raceSettings != null)
                        return MapToPublicLeaderboardSettingsDto(raceSettings);
                }

                var eventSettings = await repo
                    .GetQuery(s =>
                        s.EventId == eventId &&
                        s.RaceId == null &&
                        s.AuditProperties.IsActive &&
                        !s.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                return eventSettings != null
                    ? MapToPublicLeaderboardSettingsDto(eventSettings)
                    : new PublicLeaderboardSettingsDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error fetching leaderboard settings for event {EventId}, race {RaceId}",
                    eventId, raceId);
                return new PublicLeaderboardSettingsDto();
            }
        }

        public async Task<PublicGroupedLeaderboardDto?> GetPublicGroupedLeaderboardAsync(
            string eventId, string raceId,
            string? search, string? gender, string? category,
            bool showAll, CancellationToken ct = default)
        {
            try
            {
                int decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                int decryptedRaceId  = Convert.ToInt32(_encryptionService.Decrypt(raceId));

                var eventRepo = _repository.GetRepository<Event>();
                var eventEntity = await eventRepo
                    .GetQuery(e => e.Id == decryptedEventId &&
                                   e.AuditProperties.IsActive &&
                                   !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (eventEntity == null)
                    return null;

                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo
                    .GetQuery(r => r.Id == decryptedRaceId &&
                                   r.EventId == decryptedEventId &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (race == null)
                    return null;

                var settingsRepo = _repository.GetRepository<LeaderboardSettings>();
                LeaderboardSettings? leaderboardSettings = null;

                leaderboardSettings = await settingsRepo
                    .GetQuery(s => s.EventId == decryptedEventId &&
                                   s.RaceId == decryptedRaceId &&
                                   s.OverrideSettings == true &&
                                   s.AuditProperties.IsActive &&
                                   !s.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (leaderboardSettings == null)
                {
                    leaderboardSettings = await settingsRepo
                        .GetQuery(s => s.EventId == decryptedEventId &&
                                       s.RaceId == null &&
                                       s.AuditProperties.IsActive &&
                                       !s.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(ct);
                }

                bool rankOnNet = leaderboardSettings?.SortByOverallChipTime ?? true;
                int topN = (!showAll && (leaderboardSettings?.NumberOfResultsToShowCategory ?? 0) > 0)
                    ? leaderboardSettings!.NumberOfResultsToShowCategory!.Value
                    : (!showAll ? 3 : 0);

                var resultsRepo = _repository.GetRepository<Results>();
                var query = resultsRepo
                    .GetQuery(r => r.EventId == decryptedEventId &&
                                   r.RaceId  == decryptedRaceId &&
                                   r.Status  == "Finished" &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .Include(r => r.Participant)
                    .AsNoTracking();

                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(r =>
                        (r.Participant.BibNumber != null && r.Participant.BibNumber.Contains(search)) ||
                        (r.Participant.FirstName != null && r.Participant.FirstName.Contains(search)) ||
                        (r.Participant.LastName  != null && r.Participant.LastName.Contains(search)));

                if (!string.IsNullOrWhiteSpace(gender))
                    query = query.Where(r => r.Participant.Gender != null &&
                                             r.Participant.Gender.ToLower() == gender.ToLower());

                if (!string.IsNullOrWhiteSpace(category))
                    query = query.Where(r => r.Participant.AgeCategory != null &&
                                             r.Participant.AgeCategory.ToLower() == category.ToLower());

                var allFinishers = await query.ToListAsync(ct);

                int totalParticipants = await _repository.GetRepository<Models.Data.Entities.Participant>()
                    .GetQuery(p => p.EventId == decryptedEventId &&
                                   p.RaceId  == decryptedRaceId &&
                                   p.AuditProperties.IsActive &&
                                   !p.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .CountAsync(ct);

                var genderOrder = new[] { "male", "female" };

                var grouped = allFinishers
                    .GroupBy(r => r.Participant.Gender ?? "Unknown")
                    .OrderBy(g =>
                    {
                        var idx = Array.IndexOf(genderOrder, g.Key.ToLower());
                        return idx < 0 ? 999 : idx;
                    })
                    .Select(genderGroup => new PublicGenderGroupDto
                    {
                        Gender = genderGroup.Key,
                        Categories = genderGroup
                            .GroupBy(r => r.Participant.AgeCategory ?? "Unknown")
                            .OrderBy(c => c.Key)
                            .Select(catGroup =>
                            {
                                var sorted = rankOnNet
                                    ? catGroup.OrderBy(r => r.NetTime ?? long.MaxValue).ToList()
                                    : catGroup.OrderBy(r => r.GunTime ?? long.MaxValue).ToList();

                                var participants = sorted
                                    .Select((r, idx) => new PublicLeaderboardEntryDto
                                    {
                                        Rank  = idx + 1,
                                        Name  = r.Participant.FullName,
                                        Bib   = r.Participant.BibNumber ?? string.Empty,
                                        ChipTime = r.NetTime.HasValue
                                            ? TimeSpan.FromMilliseconds(r.NetTime.Value).ToString(@"hh\:mm\:ss")
                                            : null,
                                        GunTime = r.GunTime.HasValue
                                            ? TimeSpan.FromMilliseconds(r.GunTime.Value).ToString(@"hh\:mm\:ss")
                                            : null,
                                        ParticipantDetailUrl = $"/p/{_encryptionService.Encrypt(r.ParticipantId.ToString())}"
                                    })
                                    .ToList();

                                if (topN > 0)
                                    participants = participants.Take(topN).ToList();

                                return new PublicCategoryGroupDto
                                {
                                    CategoryName = catGroup.Key,
                                    RankBy       = rankOnNet ? "Chip time" : "Gun time",
                                    Participants = participants
                                };
                            })
                            .ToList()
                    })
                    .ToList();

                return new PublicGroupedLeaderboardDto
                {
                    EventName        = eventEntity.Name,
                    RaceName         = race.Title,
                    RaceDate         = race.StartTime ?? eventEntity.EventDate,
                    RaceDistance     = race.Distance,
                    RankBy           = rankOnNet ? "ChipTime" : "GunTime",
                    GenderCategories = grouped,
                    TotalFinishers   = allFinishers.Count,
                    TotalParticipants = totalParticipants
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error retrieving grouped leaderboard.";
                _logger.LogError(ex, "Error in GetPublicGroupedLeaderboardAsync for event {EventId}, race {RaceId}",
                    eventId, raceId);
                return null;
            }
        }

        public async Task<PublicParticipantDetailDto?> GetPublicParticipantDetailAsync(
            string participantId, CancellationToken ct = default)
        {
            try
            {
                int decryptedParticipantId;
                try
                {
                    decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));
                }
                catch
                {
                    ErrorMessage = "Invalid participant ID.";
                    return null;
                }

                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var participant = await participantRepo
                    .GetQuery(p => p.Id == decryptedParticipantId &&
                                   p.AuditProperties.IsActive &&
                                   !p.AuditProperties.IsDeleted)
                    .Include(p => p.Event)
                    .Include(p => p.Race)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (participant == null)
                    return null;

                var resultsRepo = _repository.GetRepository<Results>();
                var result = await resultsRepo
                    .GetQuery(r => r.ParticipantId == decryptedParticipantId &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                var splitRepo = _repository.GetRepository<SplitTimes>();
                var splits = await splitRepo
                    .GetQuery(s => s.ParticipantId == decryptedParticipantId &&
                                   s.AuditProperties.IsActive &&
                                   !s.AuditProperties.IsDeleted)
                    .Include(s => s.ToCheckpoint)
                    .OrderBy(s => s.ToCheckpoint.DistanceFromStart)
                    .AsNoTracking()
                    .ToListAsync(ct);

                int raceId = participant.RaceId;

                int totalFinished = await resultsRepo
                    .GetQuery(r => r.RaceId == raceId &&
                                   r.Status == "Finished" &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .CountAsync(ct);

                int totalGender = await resultsRepo
                    .GetQuery(r => r.RaceId == raceId &&
                                   r.Status == "Finished" &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .Include(r => r.Participant)
                    .AsNoTracking()
                    .CountAsync(r => r.Participant.Gender == participant.Gender, ct);

                int totalCategory = await resultsRepo
                    .GetQuery(r => r.RaceId == raceId &&
                                   r.Status == "Finished" &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .Include(r => r.Participant)
                    .AsNoTracking()
                    .CountAsync(r => r.Participant.AgeCategory == participant.AgeCategory, ct);

                static string FormatMs(long ms) =>
                    TimeSpan.FromMilliseconds(ms).ToString(@"hh\:mm\:ss");

                static string FormatPace(decimal paceMinKm)
                {
                    int mins = (int)paceMinKm;
                    int secs = (int)Math.Round((paceMinKm - mins) * 60);
                    return $"{mins}:{secs:D2}";
                }

                PublicTimeDetailDto? chipTimeDto = null;
                if (result?.NetTime.HasValue == true)
                {
                    string? avgPace = null;
                    if (participant.Race?.Distance is { } dist && dist > 0)
                    {
                        var paceMinKm = (result.NetTime.Value / 60000.0m) / dist;
                        avgPace = FormatPace(paceMinKm);
                    }

                    chipTimeDto = new PublicTimeDetailDto
                    {
                        Time         = FormatMs(result.NetTime.Value),
                        AveragePace  = avgPace,
                        OverallRank  = result.OverallRank,
                        TotalOverall = totalFinished,
                        GenderRank   = result.GenderRank,
                        TotalGender  = totalGender,
                        CategoryRank = result.CategoryRank,
                        TotalCategory = totalCategory
                    };
                }

                PublicTimeDetailDto? gunTimeDto = null;
                if (result?.GunTime.HasValue == true)
                {
                    string? avgPace = null;
                    if (participant.Race?.Distance is { } dist && dist > 0)
                    {
                        var paceMinKm = (result.GunTime.Value / 60000.0m) / dist;
                        avgPace = FormatPace(paceMinKm);
                    }

                    gunTimeDto = new PublicTimeDetailDto
                    {
                        Time         = FormatMs(result.GunTime.Value),
                        AveragePace  = avgPace,
                        OverallRank  = result.OverallRank,
                        TotalOverall = totalFinished,
                        GenderRank   = result.GenderRank,
                        TotalGender  = totalGender,
                        CategoryRank = result.CategoryRank,
                        TotalCategory = totalCategory
                    };
                }

                var splitDtos = splits.Select(st =>
                {
                    decimal? speed = null;
                    if (st.SegmentTime is { } segMs && segMs > 0)
                    {
                        var distKm = st.Distance ?? st.ToCheckpoint?.DistanceFromStart;
                        if (distKm.HasValue)
                            speed = Math.Round(distKm.Value / ((decimal)segMs / 3600000.0m), 2);
                    }

                    return new PublicSplitDetailDto
                    {
                        Checkpoint = st.ToCheckpoint?.Name ?? string.Empty,
                        SplitTime  = st.SegmentTime.HasValue ? FormatMs(st.SegmentTime.Value) : null,
                        RaceTime   = st.SplitTimeMs.HasValue ? FormatMs(st.SplitTimeMs.Value) : null,
                        RaceRank   = st.Rank,
                        SplitDist  = st.ToCheckpoint?.DistanceFromStart,
                        Pace       = st.Pace.HasValue ? FormatPace(st.Pace.Value) : null,
                        Speed      = speed
                    };
                }).ToList();

                return new PublicParticipantDetailDto
                {
                    EventName = participant.Event?.Name ?? string.Empty,
                    RaceDate  = (participant.Race?.StartTime ?? participant.Event?.EventDate)?.ToString("yyyy-MM-dd"),
                    Participant = new PublicParticipantInfoDto
                    {
                        Name     = participant.FullName,
                        Bib      = participant.BibNumber,
                        Gender   = participant.Gender,
                        Category = participant.AgeCategory,
                        Distance = participant.Race?.Distance?.ToString("0.##")
                    },
                    ChipTime = chipTimeDto,
                    GunTime  = gunTimeDto,
                    Splits   = splitDtos
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error retrieving participant detail.";
                _logger.LogError(ex, "Error in GetPublicParticipantDetailAsync for participant {ParticipantId}", participantId);
                return null;
            }
        }

        private static PublicLeaderboardSettingsDto MapToPublicLeaderboardSettingsDto(
            LeaderboardSettings s) => new()
        {
            ShowOverallResults     = s.ShowOverallResults    ?? true,
            ShowCategoryResults    = s.ShowCategoryResults   ?? true,
            ShowGenderResults      = s.ShowGenderResults     ?? false,
            ShowAgeGroupResults    = s.ShowAgeGroupResults   ?? false,
            SortByOverallChipTime  = s.SortByOverallChipTime ?? true,
            SortByOverallGunTime   = s.SortByOverallGunTime  ?? false,
            SortByCategoryChipTime = s.SortByCategoryChipTime ?? true,
            SortByCategoryGunTime  = s.SortByCategoryGunTime ?? false,
            EnableLiveLeaderboard  = s.EnableLiveLeaderboard ?? false,
            ShowSplitTimes         = s.ShowSplitTimes        ?? false,
            ShowPace               = s.ShowPace              ?? false,
            ShowTeamResults        = s.ShowTeamResults       ?? false,
            ShowMedalIcon          = s.ShowMedalIcon         ?? true,
            AutoRefreshIntervalSec       = s.AutoRefreshIntervalSec      ?? 30,
            MaxDisplayedRecords          = s.MaxDisplayedRecords         ?? 0,
            NumberOfResultsToShowOverall  = s.NumberOfResultsToShowOverall  ?? 0,
            NumberOfResultsToShowCategory = s.NumberOfResultsToShowCategory ?? 0,
        };
    }
}
