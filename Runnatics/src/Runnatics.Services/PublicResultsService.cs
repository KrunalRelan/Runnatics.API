using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Public;
using Runnatics.Models.Client.Requests.Public;
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
        private readonly ICertificatesService _certificatesService;

        public PublicResultsService(
            IUnitOfWork<RaceSyncDbContext> repository,
            ILogger<PublicResultsService> logger,
            IEncryptionService encryptionService,
            ICertificatesService certificatesService)
            : base(repository)
        {
            _logger = logger;
            _encryptionService = encryptionService;
            _certificatesService = certificatesService;
        }

        public async Task<PublicResultsResponseDto?> GetPublicEventResultsAsync(
            string encryptedEventId, GetPublicEventResultsRequest request, CancellationToken ct = default)
        {
            try
            {
                int decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(encryptedEventId));
                var page = Math.Max(1, request.PageNumber);
                var pageSize = Math.Clamp(request.PageSize, 1, 100);
                var race = request.Race;
                var q = string.IsNullOrEmpty(request.SearchString) ? null : request.SearchString;
                var gender = request.Gender;

                var eventRepo = _repository.GetRepository<Event>();
                var eventEntity = await eventRepo.GetQuery(e =>
                    e.Id == decryptedEventId &&
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
                _logger.LogError(ex, "Error in GetPublicEventResultsAsync for eventId {EncryptedEventId}", encryptedEventId);
                return null;
            }
        }

        public async Task<PublicResultDto?> GetPublicResultByBibAsync(
            string encryptedEventId, string bib, CancellationToken ct = default)
        {
            try
            {
                int decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(encryptedEventId));
                var eventRepo = _repository.GetRepository<Event>();
                var eventEntity = await eventRepo.GetQuery(e =>
                    e.Id == decryptedEventId &&
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
                _logger.LogError(ex, "Error in GetPublicResultByBibAsync for eventId {EncryptedEventId}, bib {Bib}", encryptedEventId, bib);
                return null;
            }
        }

        public async Task<PublicGroupedLeaderboardDto?> GetPublicGroupedLeaderboardAsync(
            string eventId, string raceId,
            GetPublicLeaderboardRequest request, CancellationToken ct = default)
        {
            try
            {
                var search   = request.Search;
                var gender   = request.Gender;
                var category = request.Category;
                var showAll  = request.ShowAll;
                int page     = Math.Max(1, request.PageNumber);
                int pageSize = Math.Clamp(request.PageSize, 1, 200);

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
                var leaderboardSettings = await settingsRepo
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

                // Unfiltered base for podium
                var podiumResults = await resultsRepo
                    .GetQuery(r => r.EventId == decryptedEventId &&
                                   r.RaceId  == decryptedRaceId &&
                                   r.Status  == "Finished" &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .Include(r => r.Participant)
                    .AsNoTracking()
                    .OrderBy(r => rankOnNet ? r.NetTime ?? long.MaxValue : r.GunTime ?? long.MaxValue)
                    .Take(3)
                    .ToListAsync(ct);

                // Filtered query for grouped view and OverallResults
                var filteredQuery = resultsRepo
                    .GetQuery(r => r.EventId == decryptedEventId &&
                                   r.RaceId  == decryptedRaceId &&
                                   r.Status  == "Finished" &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .Include(r => r.Participant)
                    .AsNoTracking();

                if (!string.IsNullOrWhiteSpace(search))
                    filteredQuery = filteredQuery.Where(r =>
                        (r.Participant.BibNumber != null && r.Participant.BibNumber.Contains(search)) ||
                        (r.Participant.FirstName != null && r.Participant.FirstName.Contains(search)) ||
                        (r.Participant.LastName  != null && r.Participant.LastName.Contains(search)));

                if (!string.IsNullOrWhiteSpace(gender))
                {
                    var genderNorm = gender.ToUpperInvariant() switch
                    {
                        "M" or "MALE" => "M",
                        "F" or "FEMALE" => "F",
                        _ => gender.ToUpperInvariant()
                    };
                    filteredQuery = filteredQuery.Where(r => r.Participant.Gender != null &&
                                                             r.Participant.Gender.ToUpper() == genderNorm);
                }

                if (!string.IsNullOrWhiteSpace(category))
                    filteredQuery = filteredQuery.Where(r => r.Participant.AgeCategory != null &&
                                                             r.Participant.AgeCategory.ToLower() == category.ToLower());

                var allFinishers = await filteredQuery.ToListAsync(ct);

                int totalParticipants = await _repository.GetRepository<Participant>()
                    .GetQuery(p => p.EventId == decryptedEventId &&
                                   p.RaceId  == decryptedRaceId &&
                                   p.AuditProperties.IsActive &&
                                   !p.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .CountAsync(ct);

                // Grouped categories — normalize "M"→"Male", "F"→"Female" for display
                var genderOrder = new[] { "male", "female" };
                var grouped = allFinishers
                    .GroupBy(r => (r.Participant.Gender switch { "M" => "Male", "F" => "Female", var g => g }) ?? "Unknown")
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

                // Flat paginated OverallResults
                int totalOverall = allFinishers.Count;
                int totalPages   = totalOverall == 0 ? 0 : (int)Math.Ceiling(totalOverall / (double)pageSize);

                var overallResults = (rankOnNet
                    ? allFinishers.OrderBy(r => r.NetTime ?? long.MaxValue)
                    : allFinishers.OrderBy(r => r.GunTime ?? long.MaxValue))
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select((r, idx) => new PublicLeaderboardEntryDto
                    {
                        Rank  = r.OverallRank ?? ((page - 1) * pageSize + idx + 1),
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

                // Podium from unfiltered top-3
                var podium = BuildPodium(podiumResults);

                return new PublicGroupedLeaderboardDto
                {
                    EventName         = eventEntity.Name,
                    RaceName          = race.Title,
                    RaceDate          = race.StartTime ?? eventEntity.EventDate,
                    RaceDistance      = race.Distance,
                    RankBy            = rankOnNet ? "ChipTime" : "GunTime",
                    EventBannerBase64 = eventEntity.BannerImage,
                    Podium            = podium,
                    GenderCategories  = grouped,
                    OverallResults    = overallResults,
                    TotalFinishers    = allFinishers.Count,
                    TotalParticipants = totalParticipants,
                    Page              = page,
                    PageSize          = pageSize,
                    TotalOverall      = totalOverall,
                    TotalPages        = totalPages
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

                var participantRepo = _repository.GetRepository<Participant>();
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

                // Check certificate availability for this participant's race
                bool certAvailable = await _repository.GetRepository<CertificateTemplate>()
                    .GetQuery(t => t.EventId == participant.EventId &&
                                   (t.RaceId == null || t.RaceId == participant.RaceId) &&
                                   t.AuditProperties.IsActive &&
                                   !t.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync(ct);

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
                        Time          = FormatMs(result.NetTime.Value),
                        AveragePace   = avgPace,
                        OverallRank   = result.OverallRank,
                        TotalOverall  = totalFinished,
                        GenderRank    = result.GenderRank,
                        TotalGender   = totalGender,
                        CategoryRank  = result.CategoryRank,
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
                        Time          = FormatMs(result.GunTime.Value),
                        AveragePace   = avgPace,
                        OverallRank   = result.OverallRank,
                        TotalOverall  = totalFinished,
                        GenderRank    = result.GenderRank,
                        TotalGender   = totalGender,
                        CategoryRank  = result.CategoryRank,
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
                    EventName         = participant.Event?.Name ?? string.Empty,
                    RaceDate          = (participant.Race?.StartTime ?? participant.Event?.EventDate)?.ToString("yyyy-MM-dd"),
                    EventBannerBase64 = participant.Event?.BannerImage,
                    CertificateAvailable = certAvailable,
                    CertificateUrl    = certAvailable ? $"/api/public/participant/{participantId}/certificate" : null,
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

        public async Task<PublicResultFiltersDto> GetResultFiltersAsync(CancellationToken ct = default)
        {
            try
            {
                var events = await _repository.GetRepository<Event>()
                    .GetQuery(e => e.AuditProperties.IsActive &&
                                   !e.AuditProperties.IsDeleted &&
                                   e.EventSettings != null &&
                                   e.EventSettings.Published)
                    .Include(e => e.EventSettings)
                    .AsNoTracking()
                    .OrderByDescending(e => e.EventDate)
                    .ToListAsync(ct);

                var eventItems = events.Select(e => new PublicEventFilterItemDto
                {
                    EncryptedId = _encryptionService.Encrypt(e.Id.ToString()),
                    Name        = e.Name,
                    EventDate   = e.EventDate.ToString("yyyy-MM-dd"),
                    Year        = e.EventDate.Year
                }).ToList();

                return new PublicResultFiltersDto
                {
                    Years  = eventItems.Select(e => e.Year).Distinct().OrderByDescending(y => y).ToList(),
                    Events = eventItems
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error retrieving result filters.";
                _logger.LogError(ex, "Error in GetResultFiltersAsync");
                return new PublicResultFiltersDto();
            }
        }

        public async Task<PublicRaceFilterDto?> GetRaceFiltersAsync(
            string encryptedEventId, CancellationToken ct = default)
        {
            try
            {
                int eventId = Convert.ToInt32(_encryptionService.Decrypt(encryptedEventId));

                var races = await _repository.GetRepository<Race>()
                    .GetQuery(r => r.EventId == eventId &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .OrderBy(r => r.StartTime)
                    .ThenBy(r => r.Title)
                    .ToListAsync(ct);

                return new PublicRaceFilterDto
                {
                    Races = races.Select(r => new PublicRaceFilterItemDto
                    {
                        EncryptedRaceId = _encryptionService.Encrypt(r.Id.ToString()),
                        Name            = r.Title,
                        Distance        = r.Distance.HasValue ? r.Distance.Value.ToString("0.##") + " km" : null
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error retrieving race filters.";
                _logger.LogError(ex, "Error in GetRaceFiltersAsync for event {EventId}", encryptedEventId);
                return null;
            }
        }

        public async Task<PublicBracketFilterDto?> GetBracketFiltersAsync(
            string encryptedEventId, string encryptedRaceId, CancellationToken ct = default)
        {
            try
            {
                int eventId = Convert.ToInt32(_encryptionService.Decrypt(encryptedEventId));
                int raceId  = Convert.ToInt32(_encryptionService.Decrypt(encryptedRaceId));

                var brackets = await _repository.GetRepository<Results>()
                    .GetQuery(r => r.EventId == eventId &&
                                   r.RaceId  == raceId &&
                                   r.Status  == "Finished" &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .Include(r => r.Participant)
                    .AsNoTracking()
                    .Select(r => new { r.Participant.Gender, r.Participant.AgeCategory })
                    .Distinct()
                    .ToListAsync(ct);

                var items = brackets
                    .Where(b => !string.IsNullOrEmpty(b.Gender) && !string.IsNullOrEmpty(b.AgeCategory))
                    .Select(b => new PublicBracketItemDto
                    {
                        Gender   = b.Gender!,
                        Category = b.AgeCategory!,
                        Name     = $"{b.Gender} {b.AgeCategory}"
                    })
                    .OrderBy(b => b.Gender)
                    .ThenBy(b => b.Category)
                    .ToList();

                return new PublicBracketFilterDto { Brackets = items };
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error retrieving bracket filters.";
                _logger.LogError(ex, "Error in GetBracketFiltersAsync for event {EventId}, race {RaceId}",
                    encryptedEventId, encryptedRaceId);
                return null;
            }
        }

        public async Task<List<PublicParticipantSearchResultDto>> SearchParticipantsForComparisonAsync(
            SearchParticipantsRequest request, CancellationToken ct = default)
        {
            try
            {
                var query = _repository.GetRepository<Results>()
                    .GetQuery(r => r.Status == "Finished" &&
                                   r.AuditProperties.IsActive &&
                                   !r.AuditProperties.IsDeleted)
                    .Include(r => r.Participant)
                    .Include(r => r.Race)
                    .AsNoTracking();

                if (!string.IsNullOrEmpty(request.EncryptedEventId))
                {
                    int eventId = Convert.ToInt32(_encryptionService.Decrypt(request.EncryptedEventId));
                    query = query.Where(r => r.EventId == eventId);
                }

                if (!string.IsNullOrEmpty(request.EncryptedRaceId))
                {
                    int raceId = Convert.ToInt32(_encryptionService.Decrypt(request.EncryptedRaceId));
                    query = query.Where(r => r.RaceId == raceId);
                }

                if (!string.IsNullOrEmpty(request.SearchString))
                {
                    var s = request.SearchString;
                    query = query.Where(r =>
                        (r.Participant.FirstName != null && r.Participant.FirstName.Contains(s)) ||
                        (r.Participant.LastName  != null && r.Participant.LastName.Contains(s)) ||
                        (r.Participant.BibNumber != null && r.Participant.BibNumber.Contains(s)));
                }

                var items = await query
                    .OrderBy(r => r.Participant.LastName)
                    .ThenBy(r => r.Participant.FirstName)
                    .Take(20)
                    .ToListAsync(ct);

                return items.Select(r => new PublicParticipantSearchResultDto
                {
                    EncryptedId = _encryptionService.Encrypt(r.ParticipantId.ToString()),
                    Name        = r.Participant.FullName,
                    Bib         = r.Participant.BibNumber ?? string.Empty,
                    RaceName    = r.Race?.Title ?? string.Empty,
                    ChipTime    = r.NetTime.HasValue
                        ? TimeSpan.FromMilliseconds(r.NetTime.Value).ToString(@"hh\:mm\:ss")
                        : null
                }).ToList();
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error searching participants.";
                _logger.LogError(ex, "Error in SearchParticipantsForComparisonAsync");
                return [];
            }
        }

        public async Task<PublicParticipantComparisonDto?> CompareParticipantsAsync(
            CompareParticipantsRequest request, CancellationToken ct = default)
        {
            try
            {
                int p1Id = Convert.ToInt32(_encryptionService.Decrypt(request.ParticipantId1));
                int p2Id = Convert.ToInt32(_encryptionService.Decrypt(request.ParticipantId2));

                var (p1Data, p1Result, p1Splits) = await LoadParticipantDataAsync(p1Id, ct);
                var (p2Data, p2Result, p2Splits) = await LoadParticipantDataAsync(p2Id, ct);

                if (p1Data == null || p2Data == null)
                {
                    ErrorMessage = "One or both participants not found.";
                    return null;
                }

                static string FormatMs(long ms) => TimeSpan.FromMilliseconds(ms).ToString(@"hh\:mm\:ss");

                static string? FormatPaceNullable(decimal? paceMinKm)
                {
                    if (!paceMinKm.HasValue) return null;
                    int mins = (int)paceMinKm.Value;
                    int secs = (int)Math.Round((paceMinKm.Value - mins) * 60);
                    return $"{mins}:{secs:D2}";
                }

                var p1Dto = new PublicComparisonParticipantDto
                {
                    Name     = p1Data.FullName,
                    Bib      = p1Data.BibNumber ?? string.Empty,
                    ChipTime = p1Result?.NetTime.HasValue == true ? FormatMs(p1Result.NetTime.Value) : null,
                    GunTime  = p1Result?.GunTime.HasValue  == true ? FormatMs(p1Result.GunTime.Value)  : null,
                    Splits   = p1Splits.Select(s => new PublicComparisonSplitDto
                    {
                        Checkpoint = s.ToCheckpoint?.Name ?? string.Empty,
                        Time       = s.SplitTimeMs.HasValue ? FormatMs(s.SplitTimeMs.Value) : null,
                        Pace       = FormatPaceNullable(s.Pace)
                    }).ToList()
                };

                var p2Dto = new PublicComparisonParticipantDto
                {
                    Name     = p2Data.FullName,
                    Bib      = p2Data.BibNumber ?? string.Empty,
                    ChipTime = p2Result?.NetTime.HasValue == true ? FormatMs(p2Result.NetTime.Value) : null,
                    GunTime  = p2Result?.GunTime.HasValue  == true ? FormatMs(p2Result.GunTime.Value)  : null,
                    Splits   = p2Splits.Select(s => new PublicComparisonSplitDto
                    {
                        Checkpoint = s.ToCheckpoint?.Name ?? string.Empty,
                        Time       = s.SplitTimeMs.HasValue ? FormatMs(s.SplitTimeMs.Value) : null,
                        Pace       = FormatPaceNullable(s.Pace)
                    }).ToList()
                };

                // Per-checkpoint diffs using cumulative race time
                var checkpointNames = p1Splits.Select(s => s.ToCheckpoint?.Name ?? string.Empty)
                    .Union(p2Splits.Select(s => s.ToCheckpoint?.Name ?? string.Empty))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                var diffs = new List<PublicComparisonDiffDto>();

                foreach (var cpName in checkpointNames)
                {
                    var s1 = p1Splits.FirstOrDefault(s => s.ToCheckpoint?.Name == cpName);
                    var s2 = p2Splits.FirstOrDefault(s => s.ToCheckpoint?.Name == cpName);

                    if (s1?.SplitTimeMs == null || s2?.SplitTimeMs == null) continue;

                    long t1 = s1.SplitTimeMs.Value;
                    long t2 = s2.SplitTimeMs.Value;
                    long diffMs = Math.Abs(t1 - t2);

                    diffs.Add(new PublicComparisonDiffDto
                    {
                        Checkpoint = cpName,
                        TimeDiff   = (t1 <= t2 ? "-" : "+") + FormatMs(diffMs),
                        Faster     = t1 <= t2 ? 1 : 2
                    });
                }

                // Overall finish diff
                if (p1Result?.NetTime.HasValue == true && p2Result?.NetTime.HasValue == true)
                {
                    long t1 = p1Result.NetTime.Value;
                    long t2 = p2Result.NetTime.Value;
                    diffs.Add(new PublicComparisonDiffDto
                    {
                        Checkpoint = "Finish",
                        TimeDiff   = (t1 <= t2 ? "-" : "+") + FormatMs(Math.Abs(t1 - t2)),
                        Faster     = t1 <= t2 ? 1 : 2
                    });
                }

                return new PublicParticipantComparisonDto
                {
                    Participant1 = p1Dto,
                    Participant2 = p2Dto,
                    Differences  = diffs
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error comparing participants.";
                _logger.LogError(ex, "Error in CompareParticipantsAsync");
                return null;
            }
        }

        public async Task<byte[]?> GetPublicParticipantCertificateAsync(
            string encryptedParticipantId, CancellationToken ct = default)
        {
            try
            {
                int participantId = Convert.ToInt32(_encryptionService.Decrypt(encryptedParticipantId));

                var participant = await _repository.GetRepository<Participant>()
                    .GetQuery(p => p.Id == participantId &&
                                   p.AuditProperties.IsActive &&
                                   !p.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (participant == null)
                {
                    ErrorMessage = "Participant not found.";
                    return null;
                }

                var encRaceId  = _encryptionService.Encrypt(participant.RaceId.ToString());
                var encEventId = _encryptionService.Encrypt(participant.EventId.ToString());

                var bytes = await _certificatesService.GenerateParticipantCertificateAsync(
                    encryptedParticipantId, encRaceId, encEventId);

                if (_certificatesService.HasError)
                {
                    ErrorMessage = _certificatesService.ErrorMessage;
                    return null;
                }

                return bytes;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error generating certificate.";
                _logger.LogError(ex, "Error in GetPublicParticipantCertificateAsync for participant {ParticipantId}",
                    encryptedParticipantId);
                return null;
            }
        }

        private async Task<(Participant? participant, Results? result, List<SplitTimes> splits)>
            LoadParticipantDataAsync(int participantId, CancellationToken ct)
        {
            var participant = await _repository.GetRepository<Participant>()
                .GetQuery(p => p.Id == participantId &&
                               p.AuditProperties.IsActive &&
                               !p.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (participant == null)
                return (null, null, []);

            var result = await _repository.GetRepository<Results>()
                .GetQuery(r => r.ParticipantId == participantId &&
                               r.AuditProperties.IsActive &&
                               !r.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            var splits = await _repository.GetRepository<SplitTimes>()
                .GetQuery(s => s.ParticipantId == participantId &&
                               s.AuditProperties.IsActive &&
                               !s.AuditProperties.IsDeleted)
                .Include(s => s.ToCheckpoint)
                .OrderBy(s => s.ToCheckpoint.DistanceFromStart)
                .AsNoTracking()
                .ToListAsync(ct);

            return (participant, result, splits);
        }

        private PublicPodiumDto BuildPodium(List<Results> top3)
        {
            static string FormatMs(long ms) => TimeSpan.FromMilliseconds(ms).ToString(@"hh\:mm\:ss");

            PublicPodiumEntryDto? BuildEntry(Results? r, int rank) => r == null ? null : new PublicPodiumEntryDto
            {
                ParticipantId = _encryptionService.Encrypt(r.ParticipantId.ToString()),
                Name          = r.Participant?.FullName ?? string.Empty,
                Bib           = r.Participant?.BibNumber ?? string.Empty,
                FinishedTime  = r.NetTime.HasValue ? FormatMs(r.NetTime.Value)
                              : r.GunTime.HasValue ? FormatMs(r.GunTime.Value)
                              : string.Empty,
                Rank          = rank
            };

            return new PublicPodiumDto
            {
                First  = BuildEntry(top3.Count > 0 ? top3[0] : null, 1),
                Second = BuildEntry(top3.Count > 1 ? top3[1] : null, 2),
                Third  = BuildEntry(top3.Count > 2 ? top3[2] : null, 3)
            };
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
                    query = query.Where(r => r.Race.Title == raceName);

                if (!string.IsNullOrWhiteSpace(searchQuery))
                    query = query.Where(r =>
                        (r.Participant.BibNumber != null && r.Participant.BibNumber.Contains(searchQuery)) ||
                        (r.Participant.FirstName != null && r.Participant.FirstName.Contains(searchQuery)) ||
                        (r.Participant.LastName  != null && r.Participant.LastName.Contains(searchQuery)));

                if (!string.IsNullOrWhiteSpace(gender))
                {
                    // Normalize: accept "M"/"Male"/"male" and "F"/"Female"/"female"
                    var genderNorm = gender.ToUpperInvariant() switch
                    {
                        "M" or "MALE" => "M",
                        "F" or "FEMALE" => "F",
                        _ => gender.ToUpperInvariant()
                    };
                    query = query.Where(r => r.Participant.Gender != null &&
                                             r.Participant.Gender.ToUpper() == genderNorm);
                }

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
            Gender = r.Participant?.Gender switch { "M" => "Male", "F" => "Female", var g => g },
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
