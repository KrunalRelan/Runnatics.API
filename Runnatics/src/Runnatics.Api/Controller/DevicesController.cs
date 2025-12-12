using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Responses;
using Runnatics.Services;
using Runnatics.Services.Interface;

namespace Runnatics.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DevicesController(IDevicesService service) : ControllerBase
    {
        private readonly IDevicesService _service = service;

        /// <summary>
        /// Create event organizer
        /// </summary>
        [HttpPost("create")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> Create([FromBody] string name)
        {
            try
            {
                var result = await _service.Create(name);

                if (!result)
                {
                    return BadRequest(new { error = _service.ErrorMessage });
                }

                return Created();
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while creating event organizer." });
            }
        }

        /// <summary>
        /// Update device
        /// </summary>
        [HttpPut("{deviceId}/update")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> Update([FromRoute] string deviceId, [FromBody] string name)
        {
            try
            {
                var result = await _service.Update(deviceId, name);

                if (!result)
                {
                    return BadRequest(new { error = _service.ErrorMessage });
                }

                return NoContent();
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while updating device." });
            }
        }

        /// <summary>
        /// Delete device
        /// </summary>
        [HttpDelete("{deviceId}/delete")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> Delete([FromRoute] string deviceId)
        {
            try
            {
                var result = await _service.Delete(deviceId);

                if (!result)
                {
                    return BadRequest(new { error = _service.ErrorMessage });
                }

                return NoContent();
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while deleting device." });
            }
        }

        /// <summary>
        /// Get all devices for tenant
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllDevices()
        {
            try
            {
                var toReturn = new ResponseBase<List<DevicesResponse>>();
                var result = await _service.GetAllDevices();

                if (result == null)
                {
                    return NotFound(new { error = _service.ErrorMessage });
                }
                if (result.Count == 0)
                {
                    return NoContent();
                }

                toReturn.Message = result;
                toReturn.TotalCount = result.Count;
                return Ok(toReturn);
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while retrieving devices." });
            }
        }

        /// <summary>
        /// Get single device
        /// </summary>
        [HttpGet("{deviceId}")]
        public async Task<IActionResult> GetDevice([FromRoute] string deviceId)
        {
            try
            {
                var device = await _service.GetDevice(deviceId);

                if (device == null)
                {
                    return NotFound(new { error = _service.ErrorMessage ?? "Device not found." });
                }

                return Ok(device);
            }
            catch
            {
                return StatusCode(500, new { error = "An error occurred while retrieving device." });
            }
        }        
    }
}
