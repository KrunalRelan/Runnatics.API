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
    }
}
