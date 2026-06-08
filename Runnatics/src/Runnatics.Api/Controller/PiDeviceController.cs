using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Responses.PiDevice;
using Runnatics.Services.Interface;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Endpoints called by the Raspberry Pi timing device.
    /// Authenticated via X-Device-Key header (set in Azure env as DeviceApi__Key).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class PiDeviceController : ControllerBase
    {
        private readonly IPiDeviceService _piDeviceService;

        public PiDeviceController(IPiDeviceService piDeviceService)
        {
            _piDeviceService = piDeviceService;
        }

        /// <summary>
        /// Returns all active and in-progress events with their races.
        /// Called by the Raspberry Pi on startup to populate event/race selection.
        /// </summary>
        [HttpGet("events")]
        [ProducesResponseType(typeof(ResponseBase<List<PiEventDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEvents(CancellationToken ct)
        {
            var response = new ResponseBase<List<PiEventDto>>();
            var result = await _piDeviceService.GetActiveEventsWithRacesAsync(ct);

            if (_piDeviceService.HasError)
            {
                response.Error = new ResponseBase<List<PiEventDto>>.ErrorData
                {
                    Message = _piDeviceService.ErrorMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }
    }
}
