using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Support;
using Runnatics.Models.Client.Responses.Support;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Endpoints for the Contact Us / support query workflow
    /// </summary>
    [ApiController]
    [Route("api/support")]
    [Produces("application/json")]
    public class SupportQueryController(ISupportQueryService supportQueryService) : ControllerBase
    {
        private readonly ISupportQueryService _supportQueryService = supportQueryService;

        /// <summary>
        /// Submit a new support query from the Contact Us page (public)
        /// </summary>
        [HttpPost("contact")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ResponseBase<object>), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SubmitQuery([FromBody] ContactUsRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
                return BadRequest(new { error = "Request body cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList()
                });

            var id = await _supportQueryService.SubmitQueryAsync(request);

            if (_supportQueryService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new { error = _supportQueryService.ErrorMessage });

            var response = new ResponseBase<object> { Message = new { id } };
            return StatusCode((int)HttpStatusCode.Created, response);
        }

        /// <summary>
        /// Get support query counts grouped by status (admin)
        /// </summary>
        [HttpGet("counts")]
        [Authorize]
        [ProducesResponseType(typeof(ResponseBase<SupportQueryCountsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCounts(CancellationToken cancellationToken)
        {
            var result = await _supportQueryService.GetCountsAsync();

            if (_supportQueryService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new { error = _supportQueryService.ErrorMessage });

            return Ok(new ResponseBase<SupportQueryCountsDto> { Message = result });
        }

        /// <summary>
        /// Get a paged, filtered list of support queries (admin)
        /// </summary>
        [HttpGet]
        [Authorize]
        [ProducesResponseType(typeof(ResponseBase<List<SupportQueryListItemDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetQueries(
            [FromQuery] string? submitterEmail,
            [FromQuery] int? statusId,
            [FromQuery] int? queryTypeId,
            [FromQuery] int? assignedToUserId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var (items, totalCount) = await _supportQueryService.GetQueriesAsync(
                submitterEmail, statusId, queryTypeId, assignedToUserId, page, pageSize);

            if (_supportQueryService.HasError)
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new { error = _supportQueryService.ErrorMessage });

            return Ok(new ResponseBase<List<SupportQueryListItemDto>>
            {
                Message    = items,
                TotalCount = totalCount
            });
        }

        /// <summary>
        /// Get full detail for a single support query (admin)
        /// </summary>
        [HttpGet("{id:int}")]
        [Authorize]
        [ProducesResponseType(typeof(ResponseBase<SupportQueryDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        {
            var result = await _supportQueryService.GetQueryByIdAsync(id);

            if (_supportQueryService.HasError)
            {
                if (_supportQueryService.ErrorMessage.Contains("not found"))
                    return NotFound(new { error = _supportQueryService.ErrorMessage });

                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new { error = _supportQueryService.ErrorMessage });
            }

            return Ok(new ResponseBase<SupportQueryDetailDto> { Message = result });
        }

        /// <summary>
        /// Update status, assignee, or type of a support query (admin)
        /// </summary>
        [HttpPut("{id:int}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateQuery(int id, [FromBody] UpdateQueryRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
                return BadRequest(new { error = "Request body cannot be null." });

            await _supportQueryService.UpdateQueryAsync(id, request);

            if (_supportQueryService.HasError)
            {
                if (_supportQueryService.ErrorMessage.Contains("not found"))
                    return NotFound(new { error = _supportQueryService.ErrorMessage });

                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new { error = _supportQueryService.ErrorMessage });
            }

            return Ok(new ResponseBase<object> { Message = new { message = "Query updated successfully." } });
        }

        /// <summary>
        /// Add a comment to a support query (admin)
        /// </summary>
        [HttpPost("{id:int}/comments")]
        [Authorize]
        [ProducesResponseType(typeof(ResponseBase<SupportQueryCommentDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddComment(int id, [FromBody] AddCommentRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
                return BadRequest(new { error = "Request body cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList()
                });

            // Extract admin user ID from JWT claims
            var userIdClaim = User.FindFirst("sub") ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim?.Value, out var adminUserId))
                return Unauthorized(new { error = "Unable to resolve user identity." });

            var result = await _supportQueryService.AddCommentAsync(id, request, adminUserId);

            if (_supportQueryService.HasError)
            {
                if (_supportQueryService.ErrorMessage.Contains("not found"))
                    return NotFound(new { error = _supportQueryService.ErrorMessage });

                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new { error = _supportQueryService.ErrorMessage });
            }

            return StatusCode((int)HttpStatusCode.Created,
                new ResponseBase<SupportQueryCommentDto> { Message = result });
        }

        /// <summary>
        /// Send (or re-send) the notification email for a specific comment (admin)
        /// </summary>
        [HttpPost("comments/{commentId:int}/send-email")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendCommentEmail(int commentId, CancellationToken cancellationToken)
        {
            await _supportQueryService.SendCommentEmailAsync(commentId);

            if (_supportQueryService.HasError)
            {
                if (_supportQueryService.ErrorMessage.Contains("not found"))
                    return NotFound(new { error = _supportQueryService.ErrorMessage });

                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new { error = _supportQueryService.ErrorMessage });
            }

            return Ok(new ResponseBase<object> { Message = new { message = "Email sent successfully." } });
        }

        /// <summary>
        /// Hard-delete a comment (admin)
        /// </summary>
        [HttpDelete("comments/{commentId:int}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteComment(int commentId, CancellationToken cancellationToken)
        {
            await _supportQueryService.DeleteCommentAsync(commentId);

            if (_supportQueryService.HasError)
            {
                if (_supportQueryService.ErrorMessage.Contains("not found"))
                    return NotFound(new { error = _supportQueryService.ErrorMessage });

                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new { error = _supportQueryService.ErrorMessage });
            }

            return Ok(new ResponseBase<object> { Message = new { message = "Comment deleted successfully." } });
        }
    }
}
