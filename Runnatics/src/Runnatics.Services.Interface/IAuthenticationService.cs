using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;

namespace Runnatics.Services.Interface
{
    public interface IAuthenticationService : ISimpleServiceBase
    {
        Task<AuthenticationResponse>RegisterOrganizationAsync(RegisterOrganizationRequest request);
        Task<AuthenticationResponse>LoginAsync(LoginRequest request);
        Task<InvitationResponse>InviteUserAsync(InviteUserRequest request, Guid organizationId, Guid invitedBy);
        Task<AuthenticationResponse>AcceptInvitationAsync(AcceptInvitationRequest request);
        Task<string>ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
        Task<string>ForgotPasswordAsync(ForgotPasswordRequest request);
        Task<string>ResetPasswordAsync(ResetPasswordRequest request);
        Task<string>RevokeUserAccessAsync(Guid userId, Guid revokedBy);
        Task<string>UpdateUserRoleAsync(Guid userId, string newRole, Guid updatedBy);
    }
}