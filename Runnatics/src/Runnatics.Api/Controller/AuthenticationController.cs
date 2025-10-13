using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;
using Runnatics.Services.Interface;

namespace Runnatics.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController(IAuthenticationService authService) : ControllerBase
    {
        private readonly IAuthenticationService _authService = authService;

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterAsync([FromBody] RegisterOrganizationRequest request)
        {
            ResponseBase<AuthenticationResponse> toReturn = new();
            var result = await _authService.RegisterOrganizationAsync(request);
            if (result == null)
            {
                _authService.ErrorMessage = "Registration failed.";
                return BadRequest(_authService.ErrorMessage);
            }
            if (_authService.HasError)
            {
                return BadRequest(_authService.ErrorMessage);
            }
            toReturn.Message = result;
            return Ok(toReturn);
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request)
        {
            ResponseBase<AuthenticationResponse> toReturn = new();
            var result = await _authService.LoginAsync(request);
            if (result == null)
            {
                _authService.ErrorMessage = "Invalid credentials.";
                return Unauthorized(_authService.ErrorMessage);
            }
            if (_authService.HasError)
            {
                return BadRequest(_authService.ErrorMessage);
            }
            toReturn.Message = result;
            return Ok(toReturn);
        }

        [HttpPost("invite")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest request)
        {
            var organizationId = Guid.Parse(User.FindFirst("organizationId")!.Value);
            var invitedBy = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await _authService.InviteUserAsync(request, organizationId, invitedBy);
            if (result == null)
            {
                return BadRequest("Invitation failed.");
            }
            if (_authService.HasError)
            {
                return BadRequest(_authService.ErrorMessage);
            }

            return result != null ? Ok(result) : BadRequest(result);
        }

        [HttpPost("accept-invitation")]
        [AllowAnonymous]
        public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request)
        {
            var result = await _authService.AcceptInvitationAsync(request);
            if (result == null)
            {
                return BadRequest("Acceptance failed.");
            }
            if (_authService.HasError)
            {
                return BadRequest(_authService.ErrorMessage);
            }

            return result != null ? Ok(result) : BadRequest(result);
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            ResponseBase<string> toReturn = new();
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _authService.ChangePasswordAsync(userId, request);
            if (result == null)
            {
                return BadRequest("Change password failed.");
            }
            if (_authService.HasError)
            {
                return BadRequest(_authService.ErrorMessage);
            }
            return Ok(toReturn.Message = result);
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            ResponseBase<string> toReturn = new();
            var result = await _authService.ForgotPasswordAsync(request);
            if (result == null)
            {
                return BadRequest("Forgot password process failed.");
            }
            if (_authService.HasError)
            {
                return BadRequest(_authService.ErrorMessage);
            }
            toReturn.Message = result;
            return Ok(toReturn);
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            ResponseBase<string> toReturn = new();
            var result = await _authService.ResetPasswordAsync(request);
            if (result == null)
            {
                return BadRequest("Reset password process failed.");
            }
            if (_authService.HasError)
            {
                return BadRequest(_authService.ErrorMessage);
            }
            toReturn.Message = result;
            return Ok(toReturn);
        }

        [HttpPost("users/{userId}/revoke")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RevokeUserAccess(Guid userId)
        {
            ResponseBase<string> toReturn = new();
            var revokedBy = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _authService.RevokeUserAccessAsync(userId, revokedBy);
            if (result == null)
            {
                return BadRequest("Revoke user access failed.");
            }
            if (_authService.HasError)
            {
                return BadRequest(_authService.ErrorMessage);
            }
            toReturn.Message = result;
            return Ok(toReturn);
        }

        [HttpPut("users/{userId}/role")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateUserRole(Guid userId, [FromBody] string newRole)
        {
            ResponseBase<string> toReturn = new();
            var updatedBy = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _authService.UpdateUserRoleAsync(userId, newRole, updatedBy);
            if (result == null)
            {
                return BadRequest("Update user role failed.");
            }
            if (_authService.HasError)
            {
                return BadRequest(_authService.ErrorMessage);
            }
            toReturn.Message = result;
            return Ok(toReturn);
        }
    }
}