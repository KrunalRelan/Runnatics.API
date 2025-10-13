using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;

namespace Runnatics.Services.Interface
{
    public interface IAuthenticationService
    {
        Task<ResponseBase<AuthenticationResponse>> RegisterOrganizationAsync(RegisterOrganizationRequest request);
        Task<ResponseBase<AuthenticationResponse>> LoginAsync(LoginRequest request);
        Task<ResponseBase<InvitationResponse>> InviteUserAsync(InviteUserRequest request, Guid organizationId, Guid invitedBy);
        Task<ResponseBase<AuthenticationResponse>> AcceptInvitationAsync(AcceptInvitationRequest request);
        Task<ResponseBase<string>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
        Task<ResponseBase<string>> ForgotPasswordAsync(ForgotPasswordRequest request);
        Task<ResponseBase<string>> ResetPasswordAsync(ResetPasswordRequest request);
        Task<ResponseBase<string>> RevokeUserAccessAsync(Guid userId, Guid revokedBy);
        Task<ResponseBase<string>> UpdateUserRoleAsync(Guid userId, string newRole, Guid updatedBy);
    }
}