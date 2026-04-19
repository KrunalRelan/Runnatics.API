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
        /// Create a new EPC-to-BIB mapping. Set <c>override=true</c> to forcibly replace any
        /// existing conflicting mapping (EPC assigned to another BIB, or BIB already holding
        /// a different EPC).
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ResponseBase<BibMappingResponse>), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(BibMappingConflictResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create([FromBody] CreateBibMappingRequest request, CancellationToken cancellationToken)
        {
            var serviceResult = await _bibMappingService.CreateAsync(request, cancellationToken);

            // Conflict path — return bespoke conflict payload expected by the UI
            if (serviceResult.Conflict != null)
            {
                return Conflict(serviceResult.Conflict);
            }

            if (_bibMappingService.HasError)
            {
                var errorResponse = new ResponseBase<BibMappingResponse>
                {
                    Error = new ResponseBase<BibMappingResponse>.ErrorData
                    {
                        Message = _bibMappingService.ErrorMessage
                    }
                };

                if (_bibMappingService.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(errorResponse);
                }

                if (_bibMappingService.ErrorMessage.Contains("is required", StringComparison.OrdinalIgnoreCase)
                    || _bibMappingService.ErrorMessage.Contains("must be", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(errorResponse);
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, errorResponse);
            }

            var successResponse = new ResponseBase<BibMappingResponse>
            {
                Message = serviceResult.Mapping
            };

            return StatusCode((int)HttpStatusCode.Created, new
            {
                success = true,
                overridden = serviceResult.Overridden,
                message = serviceResult.SuccessMessage,
                mapping = successResponse.Message
            });
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
