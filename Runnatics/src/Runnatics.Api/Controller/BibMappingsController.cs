using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.BibMapping;
using Runnatics.Models.Client.Responses.BibMapping;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    [ApiController]
    [Route("api/bib-mappings")]
    [Produces("application/json")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public class BibMappingsController(IBibMappingService bibMappingService) : ControllerBase
    {
        private readonly IBibMappingService _bibMappingService = bibMappingService;

        /// <summary>
        /// Create a new EPC-to-BIB mapping
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ResponseBase<BibMappingResponse>), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create([FromBody] CreateBibMappingRequest request, CancellationToken cancellationToken)
        {
            var response = new ResponseBase<BibMappingResponse>();
            var result = await _bibMappingService.CreateAsync(request, cancellationToken);

            if (_bibMappingService.HasError)
            {
                response.Error = new ResponseBase<BibMappingResponse>.ErrorData
                {
                    Message = _bibMappingService.ErrorMessage
                };

                // Conflict errors (already mapped)
                if (_bibMappingService.ErrorMessage.Contains("already mapped", StringComparison.OrdinalIgnoreCase))
                {
                    return Conflict(response);
                }

                // Not found errors
                if (_bibMappingService.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(response);
                }

                // Validation errors
                if (_bibMappingService.ErrorMessage.Contains("is required", StringComparison.OrdinalIgnoreCase)
                    || _bibMappingService.ErrorMessage.Contains("must be", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(response);
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return StatusCode((int)HttpStatusCode.Created, response);
        }

        /// <summary>
        /// Get all EPC-to-BIB mappings for a race
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ResponseBase<List<BibMappingResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetByRace([FromQuery] string raceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "raceId is required." });
            }

            var response = new ResponseBase<List<BibMappingResponse>>();
            var result = await _bibMappingService.GetByRaceAsync(raceId, cancellationToken);

            if (_bibMappingService.HasError)
            {
                response.Error = new ResponseBase<List<BibMappingResponse>>.ErrorData
                {
                    Message = _bibMappingService.ErrorMessage
                };

                if (_bibMappingService.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(response);
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            response.TotalCount = result.Count;
            return Ok(response);
        }

        /// <summary>
        /// Remove an EPC-to-BIB mapping (soft delete + unassign)
        /// </summary>
        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Delete(
            [FromQuery] string chipId,
            [FromQuery] string participantId,
            [FromQuery] string eventId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(chipId) || string.IsNullOrEmpty(participantId) || string.IsNullOrEmpty(eventId))
            {
                return BadRequest(new { error = "chipId, participantId, and eventId are required." });
            }

            var response = new ResponseBase<object>();
            var result = await _bibMappingService.DeleteAsync(chipId, participantId, eventId, cancellationToken);

            if (_bibMappingService.HasError)
            {
                response.Error = new ResponseBase<object>.ErrorData
                {
                    Message = _bibMappingService.ErrorMessage
                };

                if (_bibMappingService.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound(response);
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (!result)
            {
                response.Error = new ResponseBase<object>.ErrorData
                {
                    Message = "BIB mapping deletion failed."
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = new { message = "BIB mapping removed successfully." };
            return Ok(response);
        }
    }
}
