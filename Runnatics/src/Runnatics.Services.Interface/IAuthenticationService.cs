using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Responses;

namespace Runnatics.Services.Interface
{
    public interface IAuthenticationService
    {
        Task<ResponseBase<AuthenticationResponse>> RegisterOrganizationAsync(RegisterOrganizationRequest request);
        Task<ResponseBase<AuthenticationResponse>> LoginAsync(LoginRequest request);
        Task<ResponseBase<AuthenticationResponse>> RefreshTokenAsync(string token, string refreshToken);
     
    }
}