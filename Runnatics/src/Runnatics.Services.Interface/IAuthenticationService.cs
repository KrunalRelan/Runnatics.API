using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;

namespace Runnatics.Services.Interface
{
    public interface IAuthenticationService : ISimpleServiceBase
    {
        Task<AuthenticationResponse?>RegisterOrganizationAsync(RegisterOrganizationRequest request);
        Task<AuthenticationResponse?>LoginAsync(LoginRequest request);
        Task<InvitationResponse?>InviteUserAsync(InviteUserRequest request, int tenantId, int invitedBy);
        Task<AuthenticationResponse?>AcceptInvitationAsync(AcceptInvitationRequest request);
        Task<string?>ChangePasswordAsync(int userId, ChangePasswordRequest request);
        Task<string?>ForgotPasswordAsync(ForgotPasswordRequest request);
        Task<string?>ResetPasswordAsync(ResetPasswordRequest request);
        Task<string?>RevokeUserAccessAsync(int userId, int revokedBy);
        Task<string?>UpdateUserRoleAsync(int userId, string newRole, int updatedBy);
        Task<AuthenticationResponse?>RefreshTokenAsync(string refreshToken);
        Task<string?>LogoutAsync(string refreshToken);
        Task<bool>ValidateRefreshTokenAsync(string refreshToken);
    }
}