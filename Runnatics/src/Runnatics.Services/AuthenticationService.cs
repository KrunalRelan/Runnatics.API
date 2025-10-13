using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class AuthenticationService : ServiceBase<IUnitOfWork<>> IAuthenticationService
    {
        // Implement the methods from the interface
        public Task<ResponseBase<AuthenticationResponse>> AcceptInvitationAsync(AcceptInvitationRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<ResponseBase<string>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<ResponseBase<string>> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<ResponseBase<InvitationResponse>> InviteUserAsync(InviteUserRequest request, Guid organizationId, Guid invitedBy)
        {
            throw new NotImplementedException();
        }

        public Task<ResponseBase<AuthenticationResponse>> LoginAsync(LoginRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<ResponseBase<AuthenticationResponse>> RegisterOrganizationAsync(RegisterOrganizationRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<ResponseBase<string>> ResetPasswordAsync(ResetPasswordRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<ResponseBase<string>> RevokeUserAccessAsync(Guid userId, Guid revokedBy)
        {
            throw new NotImplementedException();
        }

        public Task<ResponseBase<string>> UpdateUserRoleAsync(Guid userId, string newRole, Guid updatedBy)
        {
            throw new NotImplementedException();
        }
    }
}