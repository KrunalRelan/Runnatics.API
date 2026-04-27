using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Public;
using Runnatics.Models.Client.Requests.Public;
using Runnatics.Services.Interface;
using System.Net;
using Event = Runnatics.Models.Data.Entities.Event;
using Results = Runnatics.Models.Data.Entities.Results;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Public-facing API for the Runnatics marketing website.
    /// All endpoints are anonymous — no JWT required.
    /// </summary>
    [ApiController]
    [Route("api/public")]
    [Produces("application/json")]
    [AllowAnonymous]
    [EnableCors("PublicSite")]
    public class PublicController(
        IEventsService eventsService,
        IResultsService resultsService,
        ISupportQueryService supportQueryService) : ControllerBase
    {
        private readonly IEventsService _eventsService = eventsService;
        private readonly IResultsService _resultsService = resultsService;
        private readonly ISupportQueryService _supportQueryService = supportQueryService;

        #region Events

        /// <summary>
        /// Returns a paged list of events.
        /// Use status=upcoming for future events (default) or status=past for past events.
        /// </summary>
        [HttpGet("events")]
        [EnableRateLimiting("PublicRead")]
        [ResponseCache(Duration = 60, VaryByQueryKeys = ["status", "city", "q", "year", "page", "pageSize"])]
        [ProducesResponseType(typeof(ResponseBase<PublicPagedResultDto<PublicEventSummaryDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEvents(
            [FromQuery] string status = "upcoming",
            [FromQuery] string? city = null,
            [FromQuery] string? q = null,
            [FromQuery] string? year = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > 100)
                return BadRequest(CreateErrorResponse<PublicPagedResultDto<PublicEventSummaryDto>>(
                    "page must be >= 1 and pageSize must be between 1 and 100."));

            var isPast = status.Equals("past", StringComparison.OrdinalIgnoreCase);

            var events = await _eventsService.GetPublicEventsAsync(isPast, city, q, page, pageSize);

            if (_eventsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicPagedResultDto<PublicEventSummaryDto>>(_eventsService.ErrorMessage));

            var items = events.AsEnumerable();
            if (int.TryParse(year, out var yearInt))
                items = items.Where(e => e.EventDate.Year == yearInt);

            var dto = new PublicPagedResultDto<PublicEventSummaryDto>
            {
                Items = items.Select(MapToSummary).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = events.TotalCount
            };

            return Ok(new ResponseBase<PublicPagedResultDto<PublicEventSummaryDto>>
            {
                Message = dto,
                TotalCount = events.TotalCount
            });
        }

        /// <summary>
        /// Returns full details for a single event identified by its URL slug.
        /// Includes race categories with participant counts.
        /// </summary>
        [HttpGet("events/{slug}")]
        [EnableRateLimiting("PublicRead")]
        [ResponseCache(Duration = 60)]
        [ProducesResponseType(typeof(ResponseBase<PublicEventDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEventBySlug(string slug, CancellationToken cancellationToken = default)
        {
            var (eventEntity, raceCounts) = await _eventsService.GetPublicEventBySlugAsync(slug);

            if (_eventsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicEventDetailDto>(_eventsService.ErrorMessage));

            if (eventEntity == null)
                return NotFound(CreateErrorResponse<PublicEventDetailDto>("Event not found."));

            var dto = MapToDetail(eventEntity, raceCounts);

            return Ok(new ResponseBase<PublicEventDetailDto> { Message = dto });
        }

        #endregion

        #region Results

        /// <summary>
        /// Returns paged results for a specific event.
        /// Searchable by bib number or participant name, filterable by race and gender.
        /// </summary>
        [HttpGet("events/{slug}/results")]
        [EnableRateLimiting("PublicRead")]
        [ResponseCache(Duration = 30)]
        [ProducesResponseType(typeof(ResponseBase<PublicResultsResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEventResults(
            string slug,
            [FromQuery] string? q,
            [FromQuery] string? race,
            [FromQuery] string? gender,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > 100)
                return BadRequest(CreateErrorResponse<PublicResultsResponseDto>(
                    "page must be >= 1 and pageSize must be between 1 and 100."));

            // Look up event — includes EventSettings, RaceSettings, LeaderboardSettings
            var (eventEntity, _) = await _eventsService.GetPublicEventBySlugAsync(slug);
            if (eventEntity == null)
                return NotFound(CreateErrorResponse<PublicResultsResponseDto>("Event not found."));

            // ── Publish gate ────────────────────────────────────────────────────
            // EventSettings.Published must be true for the event to appear in listings.
            // Results are only surfaced when the event is published AND at least one
            // race has ShowResultTable enabled.
            var isEventPublished = eventEntity.EventSettings?.Published ?? false;
            if (!isEventPublished)
                return Ok(new ResponseBase<PublicResultsResponseDto>
                {
                    Message = new PublicResultsResponseDto
                    {
                        IsPublished = false,
                        StatusMessage = "Results not yet published for this event.",
                        Results = [],
                        Races = [],
                        LeaderboardSettings = new PublicLeaderboardSettingsDto()
                    }
                });

            var publishedRaces = eventEntity.Races?
                .Where(r => r.RaceSettings == null || r.RaceSettings.Published)
                .ToList() ?? [];

            var anyShowResultTable = publishedRaces.Any(r => r.RaceSettings == null || r.RaceSettings.ShowResultTable);
            if (!anyShowResultTable)
                return Ok(new ResponseBase<PublicResultsResponseDto>
                {
                    Message = new PublicResultsResponseDto
                    {
                        IsPublished = true,
                        StatusMessage = "Results not available for this event.",
                        Results = [],
                        Races = publishedRaces.Select(r => r.Title).ToList(),
                        LeaderboardSettings = new PublicLeaderboardSettingsDto()
                    }
                });

            // ── Effective leaderboard settings ──────────────────────────────────
            var selectedRace = !string.IsNullOrEmpty(race)
                ? publishedRaces.FirstOrDefault(r =>
                    r.Title.Equals(race, StringComparison.OrdinalIgnoreCase))
                : null;

            var leaderboardSettings = await _resultsService
                .GetEffectivePublicLeaderboardSettingsAsync(eventEntity.Id, selectedRace?.Id);

            // ── Fetch and filter results ─────────────────────────────────────────
            var results = await _resultsService.GetPublicResultsAsync(
                eventEntity.Id, race, q, gender, page, pageSize);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicResultsResponseDto>(_resultsService.ErrorMessage));

            var raceSettingsMap = publishedRaces
                .Where(r => r.RaceSettings != null)
                .ToDictionary(r => r.Id, r => r.RaceSettings!);

            var filteredResults = results.Where(r =>
            {
                if (!publishedRaces.Any(pr => pr.Id == r.RaceId))
                    return false;
                if (raceSettingsMap.TryGetValue(r.RaceId, out var rs) &&
                    !rs.PublishDnf &&
                    r.Status == "DNF")
                    return false;
                return true;
            }).ToList();

            var dto = new PublicResultsResponseDto
            {
                Results = filteredResults.Select(MapToResultDto).ToList(),
                Races = publishedRaces.Select(r => r.Title).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = results.TotalCount,
                LeaderboardSettings = leaderboardSettings,
                IsPublished = true
            };

            return Ok(new ResponseBase<PublicResultsResponseDto>
            {
                Message = dto,
                TotalCount = results.TotalCount
            });
        }

        /// <summary>
        /// Returns a single participant result by bib number for a specific event.
        /// Includes split times at each checkpoint.
        /// </summary>
        [HttpGet("events/{slug}/results/{bib}")]
        [EnableRateLimiting("PublicRead")]
        [ResponseCache(Duration = 30)]
        [ProducesResponseType(typeof(ResponseBase<PublicResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetResultByBib(
            string slug,
            string bib,
            CancellationToken cancellationToken = default)
        {
            var (eventEntity, _) = await _eventsService.GetPublicEventBySlugAsync(slug);
            if (eventEntity == null)
                return NotFound(CreateErrorResponse<PublicResultDto>("Event not found."));

            // Search by exact bib number
            var results = await _resultsService.GetPublicResultsAsync(
                eventEntity.Id, raceName: null, searchQuery: bib, gender: null, page: 1, pageSize: 10);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicResultDto>(_resultsService.ErrorMessage));

            var match = results.FirstOrDefault(r =>
                r.Participant?.BibNumber != null &&
                r.Participant.BibNumber.Equals(bib, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                return NotFound(CreateErrorResponse<PublicResultDto>($"No result found for bib '{bib}'."));

            return Ok(new ResponseBase<PublicResultDto> { Message = MapToResultDto(match) });
        }

        #endregion

        #region Contact

        /// <summary>
        /// Submits a contact form message from the public website.
        /// No authentication required.
        /// </summary>
        [HttpPost("contact")]
        [EnableRateLimiting("PublicWrite")]
        [RequestSizeLimit(10_240)] // 10 KB max — contact form only
        [ProducesResponseType(typeof(ResponseBase<object>), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SubmitContactForm(
            [FromBody] PublicContactRequestDto request,
            CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(new ResponseBase<object>
                {
                    Error = new ResponseBase<object>.ErrorData
                    {
                        Code = 400,
                        Message = string.Join("; ", ModelState.Values
                            .SelectMany(v => v.Errors)
                            .Select(e => e.ErrorMessage))
                    }
                });

            var queryId = await _supportQueryService.CreatePublicQueryAsync(
                request.Name,
                request.Email,
                request.Phone,
                request.Subject,
                request.Message,
                request.EventName);

            if (_supportQueryService.HasError || queryId == 0)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<object>(
                        _supportQueryService.ErrorMessage ?? "Failed to submit your message."));

            var response = new ResponseBase<object>
            {
                Message = new { id = queryId, message = "Your message has been received. We'll get back to you soon." }
            };
            return StatusCode(StatusCodes.Status201Created, response);
        }

        #endregion

        #region Stats

        /// <summary>
        /// Returns aggregate public statistics (total events, participants, etc.).
        /// </summary>
        [HttpGet("stats")]
        [EnableRateLimiting("PublicRead")]
        [ResponseCache(Duration = 300)]
        [ProducesResponseType(typeof(ResponseBase<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPublicStats(CancellationToken cancellationToken = default)
        {
            // Fetch upcoming and past event counts via lightweight calls
            var upcoming = await _eventsService.GetPublicEventsAsync(isPast: false, city: null, searchQuery: null, page: 1, pageSize: 1);
            var past     = await _eventsService.GetPublicEventsAsync(isPast: true,  city: null, searchQuery: null, page: 1, pageSize: 1);

            var response = new ResponseBase<object>
            {
                Message = new
                {
                    upcomingEvents = upcoming.TotalCount,
                    pastEvents = past.TotalCount,
                    totalEvents = upcoming.TotalCount + past.TotalCount
                }
            };
            return Ok(response);
        }

        #endregion

        #region Private mapping helpers

        private static string? GetBannerBase64(Event e)
        {
            var removeBanner = e.EventSettings?.RemoveBanner ?? false;
            if (removeBanner || string.IsNullOrEmpty(e.BannerImage))
                return null;
            return e.BannerImage;
        }

        private static PublicEventSummaryDto MapToSummary(Event e)
        {
            // A past event has published results when at least one published race
            // has ShowResultTable enabled (or has no RaceSettings row at all = default on).
            var publishedRaces = e.Races?
                .Where(r => r.RaceSettings == null || r.RaceSettings.Published)
                .ToList() ?? [];

            var hasPublishedResults = e.EventDate.Date < DateTime.UtcNow.Date &&
                publishedRaces.Any(r => r.RaceSettings == null || r.RaceSettings.ShowResultTable);

            return new PublicEventSummaryDto
            {
                Slug = e.Slug,
                Name = e.Name,
                City = e.City,
                State = e.State,
                EventDate = e.EventDate,
                HeroImageUrl = null,
                BannerBase64 = GetBannerBase64(e),
                Description = e.Description != null && e.Description.Length > 200
                    ? e.Description[..200] + "..."
                    : e.Description,
                RaceCategories = e.Races?
                    .Select(r => r.Title)
                    .ToList() ?? [],
                ParticipantCount = null,
                RegistrationOpen = e.RegistrationDeadline.HasValue
                    ? e.RegistrationDeadline.Value > DateTime.UtcNow
                    : e.EventDate > DateTime.UtcNow,
                RegistrationUrl = null,
                Venue = e.VenueName,
                HasPublishedResults = hasPublishedResults
            };
        }

        private static PublicEventDetailDto MapToDetail(Event e, Dictionary<int, int>? raceCounts)
        {
            var totalParticipants = raceCounts?.Values.Sum() ?? 0;
            var settings = e.EventSettings;

            // Only include races where RaceSettings.Published == true (or no settings = published)
            var publishedRaces = e.Races?
                .Where(r => r.RaceSettings == null || r.RaceSettings.Published)
                .ToList() ?? [];

            return new PublicEventDetailDto
            {
                Slug = e.Slug,
                Name = e.Name,
                City = e.City,
                State = e.State,
                EventDate = e.EventDate,
                HeroImageUrl = null,
                BannerBase64 = GetBannerBase64(e),
                Description = e.Description != null && e.Description.Length > 200
                    ? e.Description[..200] + "..."
                    : e.Description,
                RaceCategories = publishedRaces.Select(r => r.Title).ToList(),
                ParticipantCount = totalParticipants,
                RegistrationOpen = e.RegistrationDeadline.HasValue
                    ? e.RegistrationDeadline.Value > DateTime.UtcNow
                    : e.EventDate > DateTime.UtcNow,
                RegistrationUrl = null,
                Venue = e.VenueName,
                FullDescription = e.Description,
                Schedule = null,
                RouteMapUrl = null,
                Races = publishedRaces.Select(r => new PublicRaceCategoryDto
                {
                    Name = r.Title,
                    Distance = r.Distance?.ToString("0.##"),
                    Price = null,
                    ParticipantLimit = r.MaxParticipants,
                    RegisteredCount = raceCounts != null && raceCounts.TryGetValue(r.Id, out var c) ? c : 0
                }).ToList(),
                RegistrationDeadline = e.RegistrationDeadline,
                ContactEmail = null,
                ShowResultSummary = settings?.ShowResultSummaryForRaces ?? false,
                ShowBanner = !(settings?.RemoveBanner ?? false) && !string.IsNullOrEmpty(e.BannerImage)
            };
        }

        private static PublicResultDto MapToResultDto(Results r)
        {
            return new PublicResultDto
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
        }

        private static ResponseBase<T> CreateErrorResponse<T>(string message) where T : class
        {
            return new ResponseBase<T>
            {
                Error = new ResponseBase<T>.ErrorData
                {
                    Code = 0,
                    Message = message
                }
            };
        }

        #endregion
    }
}
