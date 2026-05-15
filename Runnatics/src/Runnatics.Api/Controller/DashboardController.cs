using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Responses.Dashboard;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;

        public DashboardController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        [HttpGet("stats")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetDashboardStats()
        {
            var response = new ResponseBase<DashboardStatsResponse>();

            var result = await _dashboardService.GetDashboardStats();

            if (_dashboardService.HasError)
            {
                response.Error = new ResponseBase<DashboardStatsResponse>.ErrorData()
                {
                    Message = _dashboardService.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }

        [HttpGet("event/{eventId}/stats")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetEventStats(string eventId, CancellationToken cancellationToken)
        {
            var response = new ResponseBase<EventDashboardStatsDto>();
            var result = await _dashboardService.GetEventDashboardStatsAsync(eventId, cancellationToken);

            if (_dashboardService.HasError || result == null)
            {
                response.Error = new ResponseBase<EventDashboardStatsDto>.ErrorData { Message = _dashboardService.ErrorMessage ?? "Error retrieving event stats." };
                if (_dashboardService.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                    return NotFound(response);
                return StatusCode(500, response);
            }

            response.Message = result;
            return Ok(response);
        }

        [HttpGet("race/{eventId}/{raceId}/stats")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetRaceStats(string eventId, string raceId, CancellationToken cancellationToken)
        {
            var response = new ResponseBase<RaceDashboardStatsDto>();
            var result = await _dashboardService.GetRaceDashboardStatsAsync(eventId, raceId, cancellationToken);

            if (_dashboardService.HasError || result == null)
            {
                response.Error = new ResponseBase<RaceDashboardStatsDto>.ErrorData { Message = _dashboardService.ErrorMessage ?? "Error retrieving race stats." };
                if (_dashboardService.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                    return NotFound(response);
                return StatusCode(500, response);
            }

            response.Message = result;
            return Ok(response);
        }
    }
}
