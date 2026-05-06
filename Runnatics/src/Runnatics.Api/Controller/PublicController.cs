using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Public;
using Runnatics.Models.Client.Requests.Public;
using Runnatics.Services.Interface;
using System.Net;

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
        IPublicResultsService publicResultsService,
        ISupportQueryService supportQueryService) : ControllerBase
    {
        private readonly IEventsService _eventsService = eventsService;
        private readonly IPublicResultsService _resultsService = publicResultsService;
        private readonly ISupportQueryService _supportQueryService = supportQueryService;

        #region Events

        [HttpGet("events")]
        [EnableRateLimiting("PublicRead")]
        [ResponseCache(Duration = 60, VaryByQueryKeys = ["status", "city", "q", "year", "page", "pageSize", "take"])]
        [ProducesResponseType(typeof(ResponseBase<PublicPagedResultDto<PublicEventSummaryDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEvents(
            [FromQuery] string? status = "upcoming",
            [FromQuery] string? city = null,
            [FromQuery] string? q = null,
            [FromQuery] string? year = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            [FromQuery] int? take = null,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > 100)
                return BadRequest(CreateErrorResponse<PublicPagedResultDto<PublicEventSummaryDto>>(
                    "page must be >= 1 and pageSize must be between 1 and 100."));

            int? yearFilter = int.TryParse(year, out var y) ? y : null;

            var dto = await _eventsService.GetPublicEventsAsync(status, city, q, page, pageSize, take, yearFilter);

            if (_eventsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicPagedResultDto<PublicEventSummaryDto>>(_eventsService.ErrorMessage));

            return Ok(new ResponseBase<PublicPagedResultDto<PublicEventSummaryDto>>
            {
                Message = dto,
                TotalCount = dto.TotalCount
            });
        }

        [HttpGet("events/{slug}")]
        [EnableRateLimiting("PublicRead")]
        [ResponseCache(Duration = 60)]
        [ProducesResponseType(typeof(ResponseBase<PublicEventDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEventBySlug(string slug, CancellationToken cancellationToken = default)
        {
            var dto = await _eventsService.GetPublicEventBySlugAsync(slug);

            if (_eventsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicEventDetailDto>(_eventsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicEventDetailDto>("Event not found."));

            return Ok(new ResponseBase<PublicEventDetailDto> { Message = dto });
        }

        #endregion

        #region Results

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

            var dto = await _resultsService.GetPublicEventResultsAsync(slug, race, q, gender, page, pageSize, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicResultsResponseDto>(_resultsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicResultsResponseDto>("Event not found."));

            return Ok(new ResponseBase<PublicResultsResponseDto>
            {
                Message = dto,
                TotalCount = dto.TotalCount
            });
        }

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
            var dto = await _resultsService.GetPublicResultByBibAsync(slug, bib, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicResultDto>(_resultsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicResultDto>($"No result found for bib '{bib}'."));

            return Ok(new ResponseBase<PublicResultDto> { Message = dto });
        }

        [HttpGet("{eventId}/{raceId}/leaderboard")]
        [EnableRateLimiting("PublicRead")]
        [ProducesResponseType(typeof(ResponseBase<PublicGroupedLeaderboardDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetGroupedLeaderboard(
            string eventId, string raceId,
            [FromQuery] string? search = null,
            [FromQuery] string? gender = null,
            [FromQuery] string? category = null,
            [FromQuery] bool showAll = false,
            CancellationToken cancellationToken = default)
        {
            var dto = await _resultsService.GetPublicGroupedLeaderboardAsync(
                eventId, raceId, search, gender, category, showAll, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicGroupedLeaderboardDto>(_resultsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicGroupedLeaderboardDto>("Event or race not found."));

            return Ok(new ResponseBase<PublicGroupedLeaderboardDto> { Message = dto });
        }

        [HttpGet("participant/{participantId}")]
        [EnableRateLimiting("PublicRead")]
        [ProducesResponseType(typeof(ResponseBase<PublicParticipantDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetParticipantDetail(
            string participantId,
            CancellationToken cancellationToken = default)
        {
            var dto = await _resultsService.GetPublicParticipantDetailAsync(participantId, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicParticipantDetailDto>(_resultsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicParticipantDetailDto>("Participant not found."));

            return Ok(new ResponseBase<PublicParticipantDetailDto> { Message = dto });
        }

        #endregion

        #region Contact

        [HttpPost("contact")]
        [EnableRateLimiting("PublicWrite")]
        [RequestSizeLimit(10_240)]
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

            return StatusCode(StatusCodes.Status201Created, new ResponseBase<object>
            {
                Message = new { id = queryId, message = "Your message has been received. We'll get back to you soon." }
            });
        }

        #endregion

        #region Stats

        [HttpGet("stats")]
        [EnableRateLimiting("PublicRead")]
        [ResponseCache(Duration = 300)]
        [ProducesResponseType(typeof(ResponseBase<PublicStatsDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPublicStats(CancellationToken cancellationToken = default)
        {
            var dto = await _eventsService.GetPublicStatsAsync(cancellationToken);

            if (_eventsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicStatsDto>(_eventsService.ErrorMessage));

            return Ok(new ResponseBase<PublicStatsDto> { Message = dto });
        }

        #endregion

        private static ResponseBase<T> CreateErrorResponse<T>(string message) where T : class =>
            new() { Error = new ResponseBase<T>.ErrorData { Code = 0, Message = message } };
    }
}
