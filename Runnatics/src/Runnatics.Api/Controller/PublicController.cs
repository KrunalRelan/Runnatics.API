using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
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

        [HttpPost("events/search")]
        [EnableRateLimiting("PublicRead")]
        [ProducesResponseType(typeof(ResponseBase<PublicPagedResultDto<PublicEventSummaryDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEvents(
            [FromBody] GetPublicEventsRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.PageNumber < 1 || request.PageSize < 1 || request.PageSize > 100)
                return BadRequest(CreateErrorResponse<PublicPagedResultDto<PublicEventSummaryDto>>(
                    "PageNumber must be >= 1 and PageSize must be between 1 and 100."));

            var dto = await _eventsService.GetPublicEventsAsync(request, cancellationToken);

            if (_eventsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicPagedResultDto<PublicEventSummaryDto>>(_eventsService.ErrorMessage));

            return Ok(new ResponseBase<PublicPagedResultDto<PublicEventSummaryDto>>
            {
                Message = dto,
                TotalCount = dto.TotalCount
            });
        }

        [HttpGet("events/{eventId}")]
        [EnableRateLimiting("PublicRead")]
        [OutputCache(PolicyName = "PublicResults")]
        [ResponseCache(Duration = 60)]
        [ProducesResponseType(typeof(ResponseBase<PublicEventDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEventById(string eventId, CancellationToken cancellationToken = default)
        {
            var dto = await _eventsService.GetPublicEventByIdAsync(eventId);

            if (_eventsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicEventDetailDto>(_eventsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicEventDetailDto>("Event not found."));

            return Ok(new ResponseBase<PublicEventDetailDto> { Message = dto });
        }

        #endregion

        #region Results

        [HttpPost("events/{eventId}/results")]
        [EnableRateLimiting("PublicRead")]
        [ProducesResponseType(typeof(ResponseBase<PublicResultsResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEventResults(
            string eventId,
            [FromBody] GetPublicEventResultsRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.PageNumber < 1 || request.PageSize < 1 || request.PageSize > 100)
                return BadRequest(CreateErrorResponse<PublicResultsResponseDto>(
                    "PageNumber must be >= 1 and PageSize must be between 1 and 100."));

            var dto = await _resultsService.GetPublicEventResultsAsync(eventId, request, cancellationToken);

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

        [HttpGet("events/{eventId}/results/{bib}")]
        [EnableRateLimiting("PublicRead")]
        [OutputCache(PolicyName = "PublicResults")]
        [ResponseCache(Duration = 30)]
        [ProducesResponseType(typeof(ResponseBase<PublicResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetResultByBib(
            string eventId,
            string bib,
            CancellationToken cancellationToken = default)
        {
            var dto = await _resultsService.GetPublicResultByBibAsync(eventId, bib, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicResultDto>(_resultsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicResultDto>($"No result found for bib '{bib}'."));

            return Ok(new ResponseBase<PublicResultDto> { Message = dto });
        }

        [HttpPost("{eventId}/{raceId}/leaderboard")]
        [EnableRateLimiting("PublicRead")]
        [ProducesResponseType(typeof(ResponseBase<PublicGroupedLeaderboardDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetGroupedLeaderboard(
            string eventId, string raceId,
            [FromBody] GetPublicLeaderboardRequest request,
            CancellationToken cancellationToken = default)
        {
            var dto = await _resultsService.GetPublicGroupedLeaderboardAsync(
                eventId, raceId, request, cancellationToken);

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

        [HttpGet("results/filters")]
        [EnableRateLimiting("PublicRead")]
        [OutputCache(PolicyName = "PublicResults")]
        [ResponseCache(Duration = 300)]
        [ProducesResponseType(typeof(ResponseBase<PublicResultFiltersDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetResultFilters(CancellationToken cancellationToken = default)
        {
            var dto = await _resultsService.GetResultFiltersAsync(cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicResultFiltersDto>(_resultsService.ErrorMessage));

            return Ok(new ResponseBase<PublicResultFiltersDto> { Message = dto });
        }

        [HttpGet("results/{eventId}/races")]
        [EnableRateLimiting("PublicRead")]
        [OutputCache(PolicyName = "PublicResults")]
        [ResponseCache(Duration = 120)]
        [ProducesResponseType(typeof(ResponseBase<PublicRaceFilterDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetRaceFilters(string eventId, CancellationToken cancellationToken = default)
        {
            var dto = await _resultsService.GetRaceFiltersAsync(eventId, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicRaceFilterDto>(_resultsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicRaceFilterDto>("Event not found."));

            return Ok(new ResponseBase<PublicRaceFilterDto> { Message = dto });
        }

        [HttpGet("results/{eventId}/{raceId}/brackets")]
        [EnableRateLimiting("PublicRead")]
        [OutputCache(PolicyName = "PublicResults")]
        [ResponseCache(Duration = 60)]
        [ProducesResponseType(typeof(ResponseBase<PublicBracketFilterDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBracketFilters(
            string eventId, string raceId, CancellationToken cancellationToken = default)
        {
            var dto = await _resultsService.GetBracketFiltersAsync(eventId, raceId, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicBracketFilterDto>(_resultsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicBracketFilterDto>("Event or race not found."));

            return Ok(new ResponseBase<PublicBracketFilterDto> { Message = dto });
        }

        [HttpPost("participant/search")]
        [EnableRateLimiting("PublicRead")]
        [ProducesResponseType(typeof(ResponseBase<List<PublicParticipantSearchResultDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SearchParticipants(
            [FromBody] SearchParticipantsRequest request,
            CancellationToken cancellationToken = default)
        {
            var results = await _resultsService.SearchParticipantsForComparisonAsync(request, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<List<PublicParticipantSearchResultDto>>(_resultsService.ErrorMessage));

            return Ok(new ResponseBase<List<PublicParticipantSearchResultDto>> { Message = results });
        }

        [HttpPost("participant/compare")]
        [EnableRateLimiting("PublicRead")]
        [ProducesResponseType(typeof(ResponseBase<PublicParticipantComparisonDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CompareParticipants(
            [FromBody] CompareParticipantsRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(CreateErrorResponse<PublicParticipantComparisonDto>(
                    string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage))));

            var dto = await _resultsService.CompareParticipantsAsync(request, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<PublicParticipantComparisonDto>(_resultsService.ErrorMessage));

            if (dto == null)
                return NotFound(CreateErrorResponse<PublicParticipantComparisonDto>("One or both participants not found."));

            return Ok(new ResponseBase<PublicParticipantComparisonDto> { Message = dto });
        }

        [HttpGet("participant/{participantId}/certificate")]
        [EnableRateLimiting("PublicRead")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetParticipantCertificate(
            string participantId,
            CancellationToken cancellationToken = default)
        {
            var bytes = await _resultsService.GetPublicParticipantCertificateAsync(participantId, cancellationToken);

            if (_resultsService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    CreateErrorResponse<object>(_resultsService.ErrorMessage));

            if (bytes == null || bytes.Length == 0)
                return NotFound(CreateErrorResponse<object>("Certificate not available for this participant."));

            return File(bytes, "image/png", $"certificate-{participantId}.png");
        }

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
