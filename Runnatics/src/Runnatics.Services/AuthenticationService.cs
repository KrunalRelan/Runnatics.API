using System.Security.Cryptography;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Runnatics.Services
{
    public class AuthenticationService : ServiceBase<IUnitOfWork<RaceSyncDbContext>>, IAuthenticationService
    {
        protected readonly IMapper _mapper;
        protected readonly ILogger _logger;

        protected readonly IConfiguration _configuration;

        public AuthenticationService(IUnitOfWork<RaceSyncDbContext> repository,
                                    IMapper mapper,
                                    ILogger<AuthenticationService> logger,
                                    IConfiguration configuration) : base(repository)
        {
            _mapper = mapper;
            _logger = logger;
            _configuration = configuration;
        }

        // Implement the methods from the interface
        public async Task<AuthenticationResponse> AcceptInvitationAsync(AcceptInvitationRequest request)
        {
            try
            {
                var user = await _repository.GetRepository<Models.Data.Entities.User>()
                                             .GetQuery(u => u.Id == Guid.Parse(request.InvitationToken))
                                             .FirstOrDefaultAsync(); // Assuming InvitationToken is the User ID for simplicity


                if (user == null || user.IsActive)
                {
                    this.ErrorMessage = "Invalid or expired invitation";
                    _logger.LogError("Invalid or expired invitation for token: {Token}", request.InvitationToken);
                    return await Task.FromResult<AuthenticationResponse>(null);
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                user.IsActive = true;
                user.AuditProperties.UpdatedBy = user.Id; // Assuming user accepts their own invitation
                user.AuditProperties.UpdatedDate = DateTime.UtcNow;
                await _repository.SaveChangesAsync();

                var organization = await _repository.GetRepository<Models.Data.Entities.Organization>()
                                                .GetQuery(o => o.Id == user.OrganizationId
                                                            && !o.AuditProperties.IsDeleted
                                                            && o.AuditProperties.IsActive)
                                                .FirstOrDefaultAsync();

                if (organization == null || !organization.AuditProperties.IsActive || organization.AuditProperties.IsDeleted)
                {
                    _logger.LogError("Organization not found or inactive for user: {UserId}", user.Id);
                    this.ErrorMessage = "Organization not found or inactive.";
                    return await Task.FromResult<AuthenticationResponse>(null);
                }

                var token = GenerateJwtToken(user, organization);

                var refreshToken = GenerateRefreshToken();

                await SaveRefreshTokenAsync(user.Id, refreshToken, false);

                var response = new AuthenticationResponse
                {
                    Token = token,
                    RefreshToken = refreshToken,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30), // Example expiration time
                    User = _mapper.Map<UserResponse>(user),
                    Organization = _mapper.Map<OrganizationResponse>(organization),
                };

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during accepting invitation for email", request);
                this.ErrorMessage = "Error during accepting invitation.";
                return null;
            }
        }

        public Task<string> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<string> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<InvitationResponse> InviteUserAsync(InviteUserRequest request, Guid organizationId, Guid invitedBy)
        {
            throw new NotImplementedException();
        }

        public async Task<AuthenticationResponse?> LoginAsync(LoginRequest request)
        {
            try
            {
                // Find user by email
                var user = _repository.GetRepository<Models.Data.Entities.User>()
                                            .GetQuery(u => u.Email == request.Email && u.IsActive)
                                            .FirstOrDefault();

                if (user == null)
                {
                    _logger.LogError("Invalid email or password for email: {Email}", request.Email);
                    return null;
                }

                if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    _logger.LogError("Invalid email or password for email: {Email}", request.Email);
                    return null;
                }

                if (!user.IsActive || user.Organization == null || !user.Organization.AuditProperties.IsDeleted)
                {
                    _logger.LogError("User account is inactive for email: {Email}", request.Email);
                    return null;
                }

                var organization = _repository.GetRepository<Models.Data.Entities.Organization>()
                                                .GetQuery(o => o.Id == user.OrganizationId
                                                            && !o.AuditProperties.IsDeleted
                                                            && o.AuditProperties.IsActive)
                                                .FirstOrDefault();

                if (organization == null || !organization.AuditProperties.IsActive || organization.AuditProperties.IsDeleted)
                {
                    _logger.LogError("Organization not found or inactive for user: {UserId}", user.Id);
                    this.ErrorMessage = "Organization not found or inactive.";
                    return null;
                }

                // Update last login time
                user.LastLoginAt = DateTime.UtcNow;

                await _repository.SaveChangesAsync();
                var token = GenerateJwtToken(user, organization);

                var refreshToken = GenerateRefreshToken();

                await SaveRefreshTokenAsync(user.Id, refreshToken, request.RememberMe);

                var response = new AuthenticationResponse
                {
                    Token = token,
                    RefreshToken = refreshToken,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30), // Example expiration time
                    User = _mapper.Map<UserResponse>(user),
                    Organization = _mapper.Map<OrganizationResponse>(organization),
                };
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for email: {Email}", request.Email);
                return null;
            }
        }

        public Task<AuthenticationResponse> RegisterOrganizationAsync(RegisterOrganizationRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<string> ResetPasswordAsync(ResetPasswordRequest request)
        {
            throw new NotImplementedException();
        }

        public async Task<string> RevokeUserAccessAsync(Guid userId, Guid revokedBy)
        {
            try
            {
                var user = _repository.GetRepository<Models.Data.Entities.User>()
                                        .GetQuery(u => u.Id == userId && u.IsActive)
                                        .FirstOrDefault();

                if (user == null)
                {
                    _logger.LogError("User not found for userId: {UserId}", userId);
                    this.ErrorMessage = "User not found.";
                    return this.ErrorMessage;
                }

                user.IsActive = false;
                user.AuditProperties.UpdatedBy = revokedBy;
                user.AuditProperties.UpdatedDate = DateTime.UtcNow;

                await _repository.SaveChangesAsync();
                return "User access revoked successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during revoking access for userId: {UserId}", userId);
                this.ErrorMessage = "Error during revoking access.";
                return this.ErrorMessage;
            }
        }

        public async Task<string> UpdateUserRoleAsync(Guid userId, string newRole, Guid updatedBy)
        {
            try
            {
                var validRoles = new List<string> { "Admin", "Ops", "Support", "ReadOnly" };
                if (!validRoles.Contains(newRole))
                {
                    this.ErrorMessage = "Invalid role specified.";
                    return await Task.FromResult(this.ErrorMessage);
                }
                var user = _repository.GetRepository<Models.Data.Entities.User>()
                                        .GetQuery(u => u.Id == userId && u.IsActive)
                                        .FirstOrDefault();

                if (user == null)
                {
                    _logger.LogError("User not found for userId: {UserId}", userId);
                    this.ErrorMessage = "User not found.";
                    return await Task.FromResult(this.ErrorMessage);
                }
                user.Role = newRole;
                user.AuditProperties.UpdatedBy = updatedBy;
                user.AuditProperties.UpdatedDate = DateTime.UtcNow;
                await _repository.SaveChangesAsync();
                return "User role updated successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during updating role for userId: {UserId}", userId);
                this.ErrorMessage = "Error during updating role.";
                return await Task.FromResult(this.ErrorMessage);
            }
        }

        private string GenerateJwtToken(User user, Organization organization)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWT:Key"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.GivenName, user.FirstName),
                new Claim(JwtRegisteredClaimNames.FamilyName, user.LastName),
                new Claim("role", user.Role),
                new Claim("organizationId", organization.Id.ToString()),
                new Claim("organizationName", organization.Name),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["JWT:Issuer"],
                audience: _configuration["JWT:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(GetTokenExpirationHours()),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string GenerateRefreshToken()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        private static string GenerateInvitationToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        private async Task SaveRefreshTokenAsync(Guid userId, string refreshToken, bool rememberMe = false)
        {
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = BCrypt.Net.BCrypt.HashPassword(refreshToken),
                ExpiresAt = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(7),
                AuditProperties = new AuditProperties
                {
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true,
                    IsDeleted = false
                }
            };

            // Implementation to save session
            var repo = _repository.GetRepository<UserSession>();
            await repo.AddAsync(session);
            await _repository.SaveChangesAsync();
        }
        private static string GenerateSlug(string name)
        {
            return name.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("'", "")
                .Replace("\"", "")
                .Trim('-');
        }

        private int GetTokenExpirationHours()
        {
            return int.TryParse(_configuration["JWT:DurationInMinutes"], out var minutes)
                ? minutes / 60
                : 1;
        }
    }
}