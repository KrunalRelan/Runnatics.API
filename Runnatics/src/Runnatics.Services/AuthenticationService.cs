using System.Security.Cryptography;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Runnatics.Data.EF;
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
        public async Task<AuthenticationResponse?> AcceptInvitationAsync(AcceptInvitationRequest request)
        {
            try
            {
                var user = await _repository.GetRepository<Models.Data.Entities.User>()
                                             .GetQuery(u => u.Id == Guid.Parse(request.InvitationToken))
                                             .FirstOrDefaultAsync(); // Assuming InvitationToken is the User ID for simplicity


                if (user == null || user.AuditProperties.IsActive)
                {
                    this.ErrorMessage = "Invalid or expired invitation";
                    _logger.LogError("Invalid or expired invitation for token: {Token}", request.InvitationToken);
                    return null;
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                user.AuditProperties.IsActive = true;
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
                    return null;
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

        public Task<string?> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<string?> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<InvitationResponse?> InviteUserAsync(InviteUserRequest request, Guid organizationId, Guid invitedBy)
        {
            throw new NotImplementedException();
        }

        public async Task<AuthenticationResponse?> LoginAsync(LoginRequest request)
        {
            try
            {
                // Find user by email
                var user = _repository.GetRepository<User>()
                                            .GetQuery(u => u.Email == request.Email && u.AuditProperties.IsActive)
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

                if (!user.AuditProperties.IsActive || user.Organization == null || !user.Organization.AuditProperties.IsDeleted)
                {
                    _logger.LogError("User account is inactive for email: {Email}", request.Email);
                    return null;
                }

                var organization = _repository.GetRepository<Organization>()
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

        public async Task<AuthenticationResponse?> RegisterOrganizationAsync(RegisterOrganizationRequest request)
        {
            try
            {
                var response = new AuthenticationResponse();
                var organizationRepo = _repository.GetRepository<Organization>();
                var userRepo = _repository.GetRepository<User>();

                var existingOrganization = organizationRepo
                                    .GetQuery(o => o.Name == request.Name
                                             && o.Domain == request.Domain
                                             && !o.AuditProperties.IsDeleted
                                             && o.AuditProperties.IsActive)
                                    .FirstOrDefault();

                var existingUser = userRepo
                            .GetQuery(u => u.Email == request.AdminEmail
                                     && u.AuditProperties.IsActive
                                     && !u.AuditProperties.IsDeleted)
                            .FirstOrDefault();
                if (existingUser != null)
                {
                    this.ErrorMessage = "User with the same email already exists.";
                    return await Task.FromResult<AuthenticationResponse>(null);
                }

                await _repository.BeginTransactionAsync();

                if (existingOrganization != null)
                {
                    this.ErrorMessage = "Organization with the same name already exists.";
                    return await Task.FromResult<AuthenticationResponse>(null);
                }
                
                var organization = new Organization
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name,
                    Slug = GenerateSlug(request.Domain), // Generate slug from domain
                    Domain = request.Domain,
                    SubscriptionPlan = request.SubscriptionPlan ?? "starter",
                    AuditProperties = new AuditProperties
                    {
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy = request.CreatedBy,
                        IsActive = true,
                        IsDeleted = false
                    }
                };

                var addedOrganization = await organizationRepo.AddAsync(organization);

                // Create admin user
                var adminUser = new User
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = addedOrganization.Id,
                    FirstName = request.AdminFirstName,
                    LastName = request.AdminLastName,
                    Email = request.AdminEmail,
                    Role = UserRole.Admin.ToString(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword),
                    AuditProperties = new AuditProperties
                    {
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy = request.CreatedBy,
                        IsActive = true,
                        IsDeleted = false
                    }
                };
                var addeduser = await userRepo.AddAsync(adminUser);
                
                await _repository.SaveChangesAsync();

                var token = GenerateJwtToken(addeduser, organization);

                var refreshToken = GenerateRefreshToken();

                await SaveRefreshTokenAsync(addeduser.Id, refreshToken, false);

                response.Token = token;
                response.RefreshToken = refreshToken;
                response.ExpiresAt = DateTime.UtcNow.AddMinutes(30); // Example expiration time
                response.User = _mapper.Map<UserResponse>(addeduser);
                response.Organization = _mapper.Map<OrganizationResponse>(addedOrganization);

                await _repository.CommitTransactionAsync();

                return response;

            }
            catch (System.Exception ex)
            {
                // Attempt to rollback any active transaction; ignore if none or rollback fails
                try
                {
                    await _repository.RollbackTransactionAsync();
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Rollback failed or there was no active transaction to rollback.");
                    this.ErrorMessage = "Rollback failed.";
                    return await Task.FromResult<AuthenticationResponse>(null);
                }

                _logger.LogError(ex, "Error during organization registration for request: {Request}", request);
                this.ErrorMessage = "Error during organization registration.";
               
               return await Task.FromResult<AuthenticationResponse>(null);
            }
        }

        public Task<string?> ResetPasswordAsync(ResetPasswordRequest request)
        {
            throw new NotImplementedException();
        }

        public async Task<string?> RevokeUserAccessAsync(Guid userId, Guid revokedBy)
        {
            try
            {
                var user = _repository.GetRepository<Models.Data.Entities.User>()
                                        .GetQuery(u => u.Id == userId && u.AuditProperties.IsActive)
                                        .FirstOrDefault();

                if (user == null)
                {
                    _logger.LogError("User not found for userId: {UserId}", userId);
                    this.ErrorMessage = "User not found.";
                    return this.ErrorMessage;
                }

                user.AuditProperties.IsActive = false;
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

        public async Task<string?> UpdateUserRoleAsync(Guid userId, string newRole, Guid updatedBy)
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
                                        .GetQuery(u => u.Id == userId && u.AuditProperties.IsActive)
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

        public async Task<AuthenticationResponse?> RefreshTokenAsync(string refreshToken)
        {
            try
            {
                // Find the user session with the matching refresh token
                var sessionRepo = _repository.GetRepository<UserSession>();
                var sessions = await sessionRepo.GetQuery(s => s.AuditProperties.IsActive && !s.AuditProperties.IsDeleted)
                    .Include(s => s.User)
                    .ThenInclude(u => u.Organization)
                    .ToListAsync();

                UserSession? validSession = null;
                foreach (var session in sessions)
                {
                    if (BCrypt.Net.BCrypt.Verify(refreshToken, session.TokenHash))
                    {
                        validSession = session;
                        break;
                    }
                }

                if (validSession == null)
                {
                    ErrorMessage = "Invalid refresh token.";
                    return null!;
                }

                // Check if token is expired
                if (validSession.ExpiresAt <= DateTime.UtcNow)
                {
                    ErrorMessage = "Refresh token has expired.";
                    return null!;
                }

                var user = validSession.User;
                var organization = user.Organization;

                // Generate new tokens
                var newJwtToken = GenerateJwtToken(user, organization);
                var newRefreshToken = GenerateRefreshToken();

                // Update the session with new refresh token
                validSession.TokenHash = BCrypt.Net.BCrypt.HashPassword(newRefreshToken);
                validSession.AuditProperties.UpdatedDate = DateTime.UtcNow;

                await sessionRepo.UpdateAsync(validSession);
                await _repository.SaveChangesAsync();

                // Update last login time
                user.LastLoginAt = DateTime.UtcNow;
                var userRepo = _repository.GetRepository<User>();
                await userRepo.UpdateAsync(user);
                await _repository.SaveChangesAsync();

                return new AuthenticationResponse
                {
                    Token = newJwtToken,
                    RefreshToken = newRefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddHours(GetTokenExpirationHours()),
                    User = _mapper.Map<UserResponse>(user),
                    Organization = _mapper.Map<OrganizationResponse>(organization)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                ErrorMessage = "Token refresh failed.";
                return null!;
            }
        }

        public async Task<string?> LogoutAsync(string refreshToken)
        {
            try
            {
                // Find and invalidate the user session
                var sessionRepo = _repository.GetRepository<UserSession>();
                var sessions = await sessionRepo.GetQuery(s => s.AuditProperties.IsActive && !s.AuditProperties.IsDeleted)
                    .ToListAsync();

                UserSession? validSession = null;
                foreach (var session in sessions)
                {
                    if (BCrypt.Net.BCrypt.Verify(refreshToken, session.TokenHash))
                    {
                        validSession = session;
                        break;
                    }
                }

                if (validSession != null)
                {
                    // Mark session as inactive
                    validSession.AuditProperties.IsActive = false;
                    validSession.AuditProperties.UpdatedDate = DateTime.UtcNow;

                    await sessionRepo.UpdateAsync(validSession);
                    await _repository.SaveChangesAsync();
                }

                return "Logout successful.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                ErrorMessage = "Logout failed.";
                return null!;
            }
        }

        public async Task<bool> ValidateRefreshTokenAsync(string refreshToken)
        {
            try
            {
                var sessionRepo = _repository.GetRepository<UserSession>();
                var sessions = await sessionRepo.GetQuery(s => s.AuditProperties.IsActive && !s.AuditProperties.IsDeleted)
                    .ToListAsync();

                foreach (var session in sessions)
                {
                    if (BCrypt.Net.BCrypt.Verify(refreshToken, session.TokenHash))
                    {
                        return session.ExpiresAt > DateTime.UtcNow;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating refresh token");
                return false;
            }
        }

        /// <summary>
        /// Cleanup expired refresh tokens from the database
        /// </summary>
        public async Task CleanupExpiredTokensAsync()
        {
            try
            {
                var sessionRepo = _repository.GetRepository<UserSession>();
                var expiredSessions = await sessionRepo.GetQuery(s => 
                    s.ExpiresAt <= DateTime.UtcNow && s.AuditProperties.IsActive)
                    .ToListAsync();

                foreach (var session in expiredSessions)
                {
                    session.AuditProperties.IsActive = false;
                    session.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    await sessionRepo.UpdateAsync(session);
                }

                if (expiredSessions.Any())
                {
                    await _repository.SaveChangesAsync();
                    _logger.LogInformation($"Cleaned up {expiredSessions.Count} expired refresh tokens");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired tokens");
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
                new Claim(JwtRegisteredClaimNames.GivenName, user.FirstName ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.FamilyName, user.LastName ?? string.Empty),
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
        private static string GenerateSlug(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            return input.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("_", "-")
                .Replace("'", "")
                .Replace("\"", "")
                .Replace(".", "-")
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