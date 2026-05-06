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
