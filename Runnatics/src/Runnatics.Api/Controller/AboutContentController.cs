using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.About;
using Runnatics.Models.Client.Responses.About;
using Runnatics.Services.Interface;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// SuperAdmin editor for the public About page (story copy, story image,
    /// founders tiles). The public site is platform-level — one About page for
    /// racetik.com — so tenant admins cannot edit it.
    /// </summary>
    [ApiController]
    [Route("api/aboutcontent")]
    [Produces("application/json")]
    [Authorize(Roles = "SuperAdmin")]
    public class AboutContentController(IAboutContentService service) : ControllerBase
    {
        private readonly IAboutContentService _service = service;

        [HttpGet]
        [ProducesResponseType(typeof(ResponseBase<AboutContentDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetContent(CancellationToken ct = default)
        {
            var response = new ResponseBase<AboutContentDto>();
            var result = await _service.GetAboutContentAsync(ct);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<AboutContentDto>.ErrorData { Message = _service.ErrorMessage };
                return StatusCode(StatusCodes.Status500InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseBase<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UpdateContent(
            [FromBody] UpdateAboutContentRequest request,
            CancellationToken ct = default)
        {
            var response = new ResponseBase<object>();
            var ok = await _service.UpdateAboutContentAsync(request, ct);

            if (_service.HasError)
            {
                response.Error = new ResponseBase<object>.ErrorData { Message = _service.ErrorMessage };
                return BadRequest(response);
            }

            response.Message = ok;
            return Ok(response);
        }

        [HttpPost("founders")]
        [ProducesResponseType(typeof(ResponseBase<FounderDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateFounder(
            [FromBody] SaveFounderRequest request,
            CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList()
                });

            var response = new ResponseBase<FounderDto>();
            var result = await _service.CreateFounderAsync(request, ct);

            if (_service.HasError || result == null)
            {
                response.Error = new ResponseBase<FounderDto>.ErrorData { Message = _service.ErrorMessage };
                return BadRequest(response);
            }

            response.Message = result;
            return Ok(response);
        }

        [HttpPut("founders/{id}")]
        [ProducesResponseType(typeof(ResponseBase<FounderDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateFounder(
            string id,
            [FromBody] SaveFounderRequest request,
            CancellationToken ct = default)
        {
            var response = new ResponseBase<FounderDto>();
            var result = await _service.UpdateFounderAsync(id, request, ct);

            if (_service.HasError || result == null)
            {
                response.Error = new ResponseBase<FounderDto>.ErrorData { Message = _service.ErrorMessage };
                return _service.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? NotFound(response)
                    : BadRequest(response);
            }

            response.Message = result;
            return Ok(response);
        }

        [HttpDelete("founders/{id}")]
        [ProducesResponseType(typeof(ResponseBase<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteFounder(string id, CancellationToken ct = default)
        {
            var response = new ResponseBase<object>();
            var ok = await _service.DeleteFounderAsync(id, ct);

            if (_service.HasError || !ok)
            {
                response.Error = new ResponseBase<object>.ErrorData { Message = _service.ErrorMessage };
                return NotFound(response);
            }

            response.Message = true;
            return Ok(response);
        }
    }
}
