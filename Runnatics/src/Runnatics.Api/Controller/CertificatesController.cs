using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Requests.Certificates;
using Runnatics.Models.Client.Responses.Certificates;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for managing certificate templates
    /// </summary>
    [ApiController]
    [Route("api/certificates")]
    [Produces("application/json")]
    public class CertificatesController(ICertificatesService certificatesService) : ControllerBase
    {
        private readonly ICertificatesService _certificatesService = certificatesService;

        /// <summary>
        /// Create a certificate template
        /// </summary>
        /// <remarks>
        /// Create a race-specific template by providing both eventId and raceId.
        /// Create an event-wide template by providing only eventId (leave raceId as null/empty).
        /// </remarks>
        [HttpPost("templates")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(CertificateTemplateResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateTemplate([FromBody] CertificateTemplateRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { error = "Request body cannot be null." });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList()
                });
            }

            var result = await _certificatesService.CreateTemplateAsync(request);

            if (_certificatesService.HasError)
            {
                if (_certificatesService.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
                    _certificatesService.ErrorMessage?.Contains("inactive", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return BadRequest(new { error = _certificatesService.ErrorMessage });
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = _certificatesService.ErrorMessage });
            }

            if (result == null)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = "Failed to create certificate template." });
            }

            return StatusCode((int)HttpStatusCode.Created, result);
        }

        /// <summary>
        /// Update a certificate template
        /// </summary>
        [HttpPut("templates/{id}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(CertificateTemplateResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTemplate(string id, [FromBody] CertificateTemplateRequest request)
        {
            if (string.IsNullOrEmpty(id) || request == null)
            {
                return BadRequest(new { error = "Invalid input provided." });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList()
                });
            }

            var result = await _certificatesService.UpdateTemplateAsync(id, request);

            if (_certificatesService.HasError)
            {
                if (_certificatesService.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return NotFound(new { error = _certificatesService.ErrorMessage });
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = _certificatesService.ErrorMessage });
            }

            if (result == null)
            {
                return NotFound(new { error = "Certificate template not found." });
            }

            return Ok(result);
        }

        /// <summary>
        /// Get a specific certificate template by ID
        /// </summary>
        [HttpGet("templates/{id}")]
        [ProducesResponseType(typeof(CertificateTemplateResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTemplate(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest(new { error = "Invalid template ID provided." });
            }

            var result = await _certificatesService.GetTemplateAsync(id);

            if (_certificatesService.HasError)
            {
                if (_certificatesService.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return NotFound(new { error = _certificatesService.ErrorMessage });
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = _certificatesService.ErrorMessage });
            }

            if (result == null)
            {
                return NotFound(new { error = "Certificate template not found." });
            }

            return Ok(result);
        }

        /// <summary>
        /// Get all certificate templates for an event
        /// </summary>
        /// <remarks>
        /// Returns all templates associated with the event, including event-wide templates and race-specific templates.
        /// </remarks>
        [HttpGet("templates/event/{eventId}")]
        [ProducesResponseType(typeof(List<CertificateTemplateResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTemplatesByEvent(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                return BadRequest(new { error = "Invalid event ID provided." });
            }

            var results = await _certificatesService.GetTemplatesByEventAsync(eventId);

            if (_certificatesService.HasError)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = _certificatesService.ErrorMessage });
            }

            return Ok(new { templates = results, totalCount = results.Count });
        }

        /// <summary>
        /// Get certificate template for a specific race
        /// </summary>
        /// <remarks>
        /// Returns the race-specific template if it exists, otherwise returns the event-wide template.
        /// If neither exists, returns 404.
        /// </remarks>
        [HttpGet("templates/event/{eventId}/race/{raceId}")]
        [ProducesResponseType(typeof(CertificateTemplateResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTemplateByRace(string eventId, string raceId)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(raceId))
            {
                return BadRequest(new { error = "Invalid event ID or race ID provided." });
            }

            var result = await _certificatesService.GetTemplateByRaceAsync(eventId, raceId);

            if (_certificatesService.HasError)
            {
                if (_certificatesService.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return NotFound(new { error = _certificatesService.ErrorMessage });
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = _certificatesService.ErrorMessage });
            }

            if (result == null)
            {
                return NotFound(new { error = "No certificate template found for this race or event." });
            }

            return Ok(result);
        }

        /// <summary>
        /// Delete a certificate template
        /// </summary>
        [HttpDelete("templates/{id}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteTemplate(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest(new { error = "Invalid template ID provided." });
            }

            var result = await _certificatesService.DeleteTemplateAsync(id);

            if (_certificatesService.HasError)
            {
                if (_certificatesService.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return NotFound(new { error = _certificatesService.ErrorMessage });
                }

                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = _certificatesService.ErrorMessage });
            }

            if (!result)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, new { error = "Failed to delete certificate template." });
            }

            return NoContent();
        }
    }
}
