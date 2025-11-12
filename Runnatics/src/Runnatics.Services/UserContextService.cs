using Microsoft.AspNetCore.Http;
using Runnatics.Services.Interface;
using System.Security.Claims;

namespace Runnatics.Services
{
    /// <summary>
    /// Service to access current authenticated user's context from JWT token
    /// </summary>
    public class UserContextService(IHttpContextAccessor httpContextAccessor) : IUserContextService
    {
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

        /// <summary>
        /// Gets the current user's ID from the JWT token claim "sub"
        /// </summary>
        public int UserId
        {
            get
            {
                var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)
                     ?? _httpContextAccessor.HttpContext?.User?.FindFirst("sub");

                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
                {
                    return userId;
                }

                throw new UnauthorizedAccessException("User ID not found in token or user is not authenticated.");
            }
        }

        /// <summary>
        /// Gets the current user's tenant ID from the JWT token claim "tenantId"
        /// </summary>
        public int TenantId
        {
            get
            {
                var tenantIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("tenantId");

                if (tenantIdClaim != null && int.TryParse(tenantIdClaim.Value, out int tenantId))
                {
                    return tenantId;
                }

                throw new UnauthorizedAccessException("Tenant ID not found in token or user is not authenticated.");
            }
        }

        /// <summary>
        /// Gets the current user's email from the JWT token claim
        /// </summary>
        public string Email
        {
            get
            {
                var emailClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)
                        ?? _httpContextAccessor.HttpContext?.User?.FindFirst("email");

                return emailClaim?.Value ?? throw new UnauthorizedAccessException("Email not found in token.");
            }
        }

        /// <summary>
        /// Gets the current user's role from the JWT token claim
        /// </summary>
        public string Role
        {
            get
            {
                var roleClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)
                  ?? _httpContextAccessor.HttpContext?.User?.FindFirst("role");

                return roleClaim?.Value ?? throw new UnauthorizedAccessException("Role not found in token.");
            }
        }

        /// <summary>
        /// Gets the current user's full name from the JWT token claims
        /// </summary>
        public string FullName
        {
            get
            {
                var givenName = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.GivenName)?.Value
                    ?? _httpContextAccessor.HttpContext?.User?.FindFirst("given_name")?.Value
                    ?? string.Empty;

                var familyName = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Surname)?.Value
                     ?? _httpContextAccessor.HttpContext?.User?.FindFirst("family_name")?.Value
                     ?? string.Empty;

                return $"{givenName} {familyName}".Trim();
            }
        }

        /// <summary>
        /// Checks if the user is authenticated
        /// </summary>
        public bool IsAuthenticated
        {
            get
            {
                return _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
            }
        }
    }
}
