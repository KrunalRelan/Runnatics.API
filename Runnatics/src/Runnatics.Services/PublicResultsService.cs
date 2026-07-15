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

                // BUG-07: a race was requested but did not resolve to a published race (typo, unpublished,
                // title mismatch). Return no results rather than falling through to an unfiltered all-races
                // query, which would re-introduce the cross-race leak.
                if (!string.IsNullOrEmpty(race) && selectedRace == null)
                {
                    return new PublicResultsResponseDto
                    {
                        IsPublished = true,
                        Results = [],
                        Races = publishedRaces.Select(r => r.Title).ToList(),
                        Page = page,
                        PageSize = pageSize,
                        TotalCount = 0,
                        LeaderboardSettings = await GetEffectivePublicLeaderboardSettingsAsync(eventEntity.Id, null)
                    };
                }

                var leaderboardSettings = await GetEffectivePublicLeaderboardSettingsAsync(eventEntity.Id, selectedRace?.Id);

                var results = await GetPublicResultsAsync(eventEntity.Id, selectedRace?.Id, q, gender, page, pageSize);

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
                    eventEntity.Id, raceId: null, searchQuery: bib, gender: null, page: 1, pageSize: 10);

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

                // BUG-24: Overall and Category rank INDEPENDENTLY (per-view basis). Resolved from the
                // SAME source as the STORED ranks (RankCalculator.ResolveBasis) — per-view leaderboard
                // setting, defaulting to EventSettings.RankOnNet — so the "Chip time / Gun time" labels
                // always match the stored-rank order the sections are now sorted by.
                var eventSettingsForRank = await _repository.GetRepository<EventSettings>()
                    .GetQuery(es => es.EventId == decryptedEventId &&
                                    es.AuditProperties.IsActive && !es.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);
                var (overallRankOnNet, categoryRankOnNet) =
                    RankCalculator.ResolveBasis(leaderboardSettings, eventSettingsForRank?.RankOnNet ?? false);

                // BUG-24: honour the Show Overall / Show Category toggles.
                bool showOverall  = leaderboardSettings?.ShowOverallResults  ?? true;
                bool showCategory = leaderboardSettings?.ShowCategoryResults ?? true;

                // BUG-24: independent per-section result caps. 0 (or showAll) = no cap.
                // Category keeps its historical default of 3 when no count is configured.
                int categoryTopN = (!showAll && (leaderboardSettings?.NumberOfResultsToShowCategory ?? 0) > 0)
                    ? leaderboardSettings!.NumberOfResultsToShowCategory!.Value
                    : (!showAll ? 3 : 0);
                int overallTopN = (!showAll && (leaderboardSettings?.NumberOfResultsToShowOverall ?? 0) > 0)
                    ? leaderboardSettings!.NumberOfResultsToShowOverall!.Value
                    : 0;

                // When showAll is true, return all overall results (up to 1000); otherwise honour the
                // caller's paging request (capped at 200 for regular browsing).
                int pageSize = showAll ? 1000 : Math.Clamp(request.PageSize, 1, 200);
                int page     = showAll ? 1    : Math.Max(1, request.PageNumber);

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
                    .OrderBy(r => r.OverallRank ?? int.MaxValue)
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
                var grouped = !showCategory
                    ? new List<PublicGenderGroupDto>()
                    : allFinishers
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
                            // BUG-12: don't synthesize an "Unknown" category bucket — skip finishers
                            // with no real age category (they still appear in OverallResults).
                            .Where(r => !string.IsNullOrWhiteSpace(r.Participant.AgeCategory) &&
                                        !string.Equals(r.Participant.AgeCategory, "Unknown", StringComparison.OrdinalIgnoreCase))
                            .GroupBy(r => r.Participant.AgeCategory!)
                            .OrderBy(c => c.Key)
                            .Select(catGroup =>
                            {
                                // Order by the STORED CategoryRank (category basis) — same ranks every
                                // surface reads; display the stored rank as the number.
                                var sorted = catGroup.OrderBy(r => r.CategoryRank ?? int.MaxValue).ToList();

                                var participants = sorted
                                    .Select((r, idx) => new PublicLeaderboardEntryDto
                                    {
                                        Rank  = r.CategoryRank ?? (idx + 1),
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

                                if (categoryTopN > 0)
                                    participants = participants.Take(categoryTopN).ToList();

                                return new PublicCategoryGroupDto
                                {
                                    CategoryName = catGroup.Key,
                                    RankBy       = categoryRankOnNet ? "Chip time" : "Gun time",
                                    Participants = participants
                                };
                            })
                            .ToList()
                    })
                    .ToList();

                // Flat paginated OverallResults
                int totalOverall = allFinishers.Count;
                int totalPages   = totalOverall == 0 ? 0 : (int)Math.Ceiling(totalOverall / (double)pageSize);

                // Order by the STORED OverallRank (overall basis) so the row order matches the
                // displayed rank number (which already reads r.OverallRank below).
                var overallSorted = allFinishers
                    .OrderBy(r => r.OverallRank ?? int.MaxValue)
                    .ToList();

                // BUG-24: when a NumberOfResultsToShowOverall cap is configured, return the top N
                // (paging disabled); otherwise honour the caller's page/pageSize. Hidden section → empty.
                IEnumerable<Results> overallSlice = overallTopN > 0
                    ? overallSorted.Take(overallTopN)
                    : overallSorted.Skip((page - 1) * pageSize).Take(pageSize);

                var overallResults = !showOverall
                    ? new List<PublicLeaderboardEntryDto>()
                    : overallSlice
                    .Select((r, idx) => new PublicLeaderboardEntryDto
                    {
                        Rank   = r.OverallRank ?? ((page - 1) * pageSize + idx + 1),
                        Name   = r.Participant.FullName,
                        Bib    = r.Participant.BibNumber ?? string.Empty,
                        Gender = r.Participant.Gender,
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
                    RankBy            = overallRankOnNet ? "ChipTime" : "GunTime",
                    OverallRankBy     = overallRankOnNet ? "ChipTime" : "GunTime",
                    CategoryRankBy    = categoryRankOnNet ? "ChipTime" : "GunTime",
                    ShowOverall       = showOverall,
                    ShowCategory      = showCategory,
                    NumberOfResultsToShowOverall  = leaderboardSettings?.NumberOfResultsToShowOverall ?? 0,
                    NumberOfResultsToShowCategory = leaderboardSettings?.NumberOfResultsToShowCategory ?? 0,
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
                        .ThenInclude(r => r.RaceSettings) // LateStartCutOff → net split baseline
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

                // NET baseline (SplitBaseline): cumulative "race time" is measured from the
                // runner's own VALID start crossing, not the gun (stored SplitTimeMs is
                // gun-based). Gun fallback for a late-only placeholder / missing start row —
                // consistent with the (gun-clamped) NetTime, so RaceTime@Finish == chip time.
                var startRowMsPublic = splits
                    .FirstOrDefault(s => s.ToCheckpoint != null && s.ToCheckpoint.DistanceFromStart == 0m)
                    ?.SplitTimeMs;
                var splitBaselineMs = SplitBaseline.BaselineMs(
                    startRowMsPublic, participant.Race?.RaceSettings?.LateStartCutOff);

                // splits is ordered by ToCheckpoint.DistanceFromStart (see query above), so we can
                // walk it with the previous checkpoint's distance to derive each SEGMENT.
                var splitDtos = splits.Select((st, idx) =>
                {
                    var thisDist = st.ToCheckpoint?.DistanceFromStart;
                    // Start row = the start-line checkpoint (DistanceFromStart == 0). It carries no
                    // prior segment, and its SplitTimeMs/SegmentTime hold only the gun→mat offset.
                    // Keyed on distance (not idx 0) so a finisher who missed the start mat — whose
                    // first recorded row is a >0 km checkpoint — is NOT wrongly zeroed.
                    bool isStartRow = thisDist.HasValue && thisDist.Value == 0m;

                    var cumulativeMs = isStartRow
                        ? 0L
                        : SplitBaseline.CumulativeMs(st.SplitTimeMs, splitBaselineMs);

                    decimal? speed = null;
                    if (!isStartRow && st.SegmentTime is { } segMs && segMs > 0 && thisDist.HasValue)
                    {
                        // Speed bug fix: segment distance = this checkpoint − previous checkpoint
                        // (NOT cumulative DistanceFromStart), divided by this segment's time.
                        var prevDist = idx > 0 ? (splits[idx - 1].ToCheckpoint?.DistanceFromStart ?? 0m) : 0m;
                        var segDistKm = thisDist.Value - prevDist;
                        if (segDistKm > 0)
                            speed = Math.Round(segDistKm / ((decimal)segMs / 3600000.0m), 2);
                    }

                    return new PublicSplitDetailDto
                    {
                        Checkpoint = st.ToCheckpoint?.Name ?? string.Empty,
                        // BUG-25 (page-scoped): the Start row's split and race time are 0 by
                        // definition — the personal clock starts at the gun, not before it.
                        SplitTime  = isStartRow ? "00:00:00"
                                     : (st.SegmentTime.HasValue ? FormatMs(st.SegmentTime.Value) : null),
                        RaceTime   = isStartRow ? "00:00:00"
                                     : (st.SplitTimeMs.HasValue ? FormatMs(cumulativeMs) : null),
                        RaceRank   = st.Rank,
                        SplitDist  = thisDist,
                        // Pace recomputed from the NET cumulative (stored Pace may be stale gun-based).
                        Pace       = !isStartRow && thisDist is > 0 && cumulativeMs > 0
                                     ? FormatPace(cumulativeMs / 60000m / thisDist.Value)
                                     : null,
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

                var (p1Data, p1Result, p1Splits, p1BaselineMs) = await LoadParticipantDataAsync(p1Id, ct);
                var (p2Data, p2Result, p2Splits, p2BaselineMs) = await LoadParticipantDataAsync(p2Id, ct);

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

                // NET cumulative per runner (Start row 0 by definition; stored SplitTimeMs is
                // gun-based). Pace recomputed from the net cumulative (stored Pace may be stale).
                static List<PublicComparisonSplitDto> BuildSplits(List<SplitTimes> splits, long baselineMs) =>
                    splits.Select(s =>
                    {
                        var dist = s.ToCheckpoint?.DistanceFromStart;
                        var isStartRow = dist == 0m;
                        var cumulativeMs = isStartRow ? 0L : SplitBaseline.CumulativeMs(s.SplitTimeMs, baselineMs);
                        return new PublicComparisonSplitDto
                        {
                            Checkpoint = s.ToCheckpoint?.Name ?? string.Empty,
                            Time       = isStartRow ? FormatMs(0)
                                         : (s.SplitTimeMs.HasValue ? FormatMs(cumulativeMs) : null),
                            Pace       = FormatPaceNullable(!isStartRow && dist is > 0 && cumulativeMs > 0
                                         ? cumulativeMs / 60000m / dist.Value
                                         : null)
                        };
                    }).ToList();

                var p1Dto = new PublicComparisonParticipantDto
                {
                    Name     = p1Data.FullName,
                    Bib      = p1Data.BibNumber ?? string.Empty,
                    ChipTime = p1Result?.NetTime.HasValue == true ? FormatMs(p1Result.NetTime.Value) : null,
                    GunTime  = p1Result?.GunTime.HasValue  == true ? FormatMs(p1Result.GunTime.Value)  : null,
                    Splits   = BuildSplits(p1Splits, p1BaselineMs)
                };

                var p2Dto = new PublicComparisonParticipantDto
                {
                    Name     = p2Data.FullName,
                    Bib      = p2Data.BibNumber ?? string.Empty,
                    ChipTime = p2Result?.NetTime.HasValue == true ? FormatMs(p2Result.NetTime.Value) : null,
                    GunTime  = p2Result?.GunTime.HasValue  == true ? FormatMs(p2Result.GunTime.Value)  : null,
                    Splits   = BuildSplits(p2Splits, p2BaselineMs)
                };

                // Per-checkpoint diffs using NET cumulative race time (per-runner baseline) —
                // a gun-based diff would be skewed by the corral-offset difference.
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

                    long t1 = SplitBaseline.CumulativeMs(s1.SplitTimeMs, p1BaselineMs);
                    long t2 = SplitBaseline.CumulativeMs(s2.SplitTimeMs, p2BaselineMs);
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

        private async Task<(Participant? participant, Results? result, List<SplitTimes> splits, long baselineMs)>
            LoadParticipantDataAsync(int participantId, CancellationToken ct)
        {
            var participant = await _repository.GetRepository<Participant>()
                .GetQuery(p => p.Id == participantId &&
                               p.AuditProperties.IsActive &&
                               !p.AuditProperties.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (participant == null)
                return (null, null, [], 0L);

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

            // NET split baseline (SplitBaseline) — per runner, from THEIR race's LateStartCutOff:
            // comparisons must compare running time, not corral positions (a gun-based diff is
            // skewed by offset1 − offset2).
            var lateStartCutOff = await _repository.GetRepository<Race>()
                .GetQuery(r => r.Id == participant.RaceId)
                .Select(r => (int?)r.RaceSettings.LateStartCutOff)
                .FirstOrDefaultAsync(ct);
            var startRowMs = splits
                .FirstOrDefault(s => s.ToCheckpoint != null && s.ToCheckpoint.DistanceFromStart == 0m)
                ?.SplitTimeMs;
            var baselineMs = SplitBaseline.BaselineMs(startRowMs, lateStartCutOff);

            return (participant, result, splits, baselineMs);
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
            int? raceId,
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
                        .ThenInclude(rc => rc.RaceSettings) // LateStartCutOff → net split baseline
                    .Include(r => r.Participant.SplitTimes
                        .Where(st => st.EventId == eventId &&
                                     st.AuditProperties.IsActive &&
                                     !st.AuditProperties.IsDeleted))
                        .ThenInclude(st => st.ToCheckpoint)
                    .AsNoTracking();

                // BUG-07: scope by RaceId, not Race.Title. Title matching (or no filter at all when a
                // race wasn't resolved) merged participants across races and sorted them by per-race
                // OverallRank, leaking a 5KM Rank-1 into the 10KM list.
                if (raceId.HasValue)
                    query = query.Where(r => r.RaceId == raceId.Value);

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

                // #7/#5 sort: ranked (OK) first by rank, then DNF, DNS, DSQ LAST. The bare
                // OrderBy(OverallRank) put SQL NULLs FIRST — DSQ/DNF rows would have floated to
                // the TOP of the public leaderboard.
                var items = await query
                    .OrderBy(r => r.Status == "Finished" ? 0 : r.Status == "DNF" ? 1 : r.Status == "DNS" ? 2 : r.Status == "DQ" ? 3 : 4)
                    .ThenBy(r => r.OverallRank ?? int.MaxValue)
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

        private static PublicResultDto MapToResultDto(Results r)
        {
            // NET split baseline (SplitBaseline): stored SplitTimeMs is gun-based; the displayed
            // cumulative is measured from the runner's own valid start crossing (gun fallback).
            var startRowMs = r.Participant?.SplitTimes?
                .FirstOrDefault(st => st.ToCheckpoint != null && st.ToCheckpoint.DistanceFromStart == 0m)
                ?.SplitTimeMs;
            var baselineMs = SplitBaseline.BaselineMs(startRowMs, r.Race?.RaceSettings?.LateStartCutOff);

            return new()
            {
                BibNumber = r.Participant?.BibNumber ?? string.Empty,
                ParticipantName = r.Participant?.FullName ?? string.Empty,
                RaceName = r.Race?.Title ?? string.Empty,
                AgeGroup = r.Participant?.AgeCategory,
                Gender = r.Participant?.Gender, // BUG-17: emit raw "M"/"F", not full words
                GunTime = r.GunTime.HasValue ? TimeSpan.FromMilliseconds(r.GunTime.Value) : null,
                NetTime = r.NetTime.HasValue ? TimeSpan.FromMilliseconds(r.NetTime.Value) : null,
                OverallRank = r.OverallRank,
                CategoryRank = r.CategoryRank,
                GenderRank = r.GenderRank,
                // #5/#7 display status — DSQ visible with its label, null ranks, sorted last.
                Status = Models.Data.Constants.ResultStatus.ToDisplay(r.Status),
                Splits = r.Participant?.SplitTimes?
                    .OrderBy(st => st.ToCheckpoint?.DistanceFromStart)
                    .Select(st => new PublicSplitDto
                    {
                        CheckpointName = st.ToCheckpoint?.Name ?? string.Empty,
                        // Start row: 00:00 by definition; others: net cumulative.
                        Time = st.ToCheckpoint?.DistanceFromStart == 0m
                            ? TimeSpan.Zero
                            : st.SplitTimeMs.HasValue
                                ? TimeSpan.FromMilliseconds(SplitBaseline.CumulativeMs(st.SplitTimeMs, baselineMs))
                                : st.SplitTime,
                        Rank = st.Rank
                    })
                    .ToList()
            };
        }

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
