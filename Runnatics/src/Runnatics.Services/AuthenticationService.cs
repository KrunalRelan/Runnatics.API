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
    public class AuthenticationService(IUnitOfWork<RaceSyncDbContext> repository,
                                IMapper mapper,
                                ILogger<AuthenticationService> logger,
                                IConfiguration configuration) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IAuthenticationService
    {
        protected readonly IMapper _mapper = mapper;
        protected readonly ILogger _logger = logger;
        protected readonly IConfiguration _configuration = configuration;

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
                _logger.LogError(ex, "Error during accepting invitation for email: {Email}", request.InvitationToken);
                this.ErrorMessage = "Error during accepting invitation.";
                return null;
            }
        }

        public async Task<string?> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.CurrentPassword) || 
                    string.IsNullOrWhiteSpace(request.NewPassword))
                {
                    ErrorMessage = "Current password and new password are required.";
                    return null;
                }

                if (request.NewPassword != request.ConfirmPassword)
                {
                    ErrorMessage = "New password and confirmation do not match.";
                    return null;
                }

                // Get the user
                var userRepo = _repository.GetRepository<User>();
                var user = await userRepo.GetQuery(u => u.Id == userId 
                                                  && u.AuditProperties.IsActive 
                                                  && !u.AuditProperties.IsDeleted)
                                        .FirstOrDefaultAsync();

                if (user == null)
                {
                    ErrorMessage = "User not found or inactive.";
                    return null;
                }

                // Verify current password
                if (string.IsNullOrEmpty(user.PasswordHash) ||
                    !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                {
                    ErrorMessage = "Current password is incorrect.";
                    _logger.LogWarning("Invalid current password attempt for user: {UserId}", userId);
                    return null;
                }

                // Validate new password strength
                if (!IsValidPassword(request.NewPassword))
                {
                    ErrorMessage = "New password must contain at least one uppercase letter, one lowercase letter, one number, and one special character (@$!%*?&).";
                    return null;
                }
                
                
                        // Check if new password is different from current password
                if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
                {
                    ErrorMessage = "New password must be different from the current password.";
                    return null;
                }

                // Hash the new password
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                user.AuditProperties.UpdatedDate = DateTime.UtcNow;
                user.AuditProperties.UpdatedBy = userId; // User is updating their own password

                // Save changes
                await userRepo.UpdateAsync(user);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Password changed successfully for user: {UserId}", userId);
                return "Password changed successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during changing password for user: {UserId}", userId);
                ErrorMessage = "Error during changing password.";
                return null;
            }
        }

        public async Task<string?> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.Email))
                {
                    ErrorMessage = "Email address is required.";
                    return null;
                }

                // Find user by email
                var userRepo = _repository.GetRepository<User>();
                var user = await userRepo.GetQuery(u => u.Email.ToLower() == request.Email.ToLower() 
                    && u.AuditProperties.IsActive && !u.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                // Always return success message for security (don't reveal if email exists)
                // But only send email if user actually exists
                if (user != null)
                {
                    // Generate reset token
                    var resetToken = GeneratePasswordResetToken();
                    var tokenExpiry = DateTime.UtcNow.AddHours(1); // Token expires in 1 hour

                    // Save reset token to database
                    var passwordResetTokenRepo = _repository.GetRepository<PasswordResetToken>();
                    var resetTokenEntity = new PasswordResetToken
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        TokenHash = BCrypt.Net.BCrypt.HashPassword(resetToken),
                        ExpiresAt = tokenExpiry,
                        IsUsed = false,
                        AuditProperties = new AuditProperties
                        {
                            CreatedDate = DateTime.UtcNow,
                            CreatedBy = user.Id,
                            IsActive = true,
                            IsDeleted = false
                        }
                    };

                    await passwordResetTokenRepo.AddAsync(resetTokenEntity);
                    await _repository.SaveChangesAsync();

                    // In a real application, you would send an email here
                    // For now, we'll log the reset token for development purposes
                    _logger.LogInformation("Password reset requested for user: {UserId}. Reset token generated.", user.Id);
                    
                    // TODO: Implement email service to send reset link
                    // await _emailService.SendPasswordResetEmailAsync(user.Email, resetToken);
                }
                else
                {
                    _logger.LogWarning("Password reset requested for non-existent email: {Email}", request.Email);
                }

                // Always return success message for security reasons
                return "If the email address exists in our system, you will receive a password reset link shortly.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during forgot password for email: {Email}", request.Email);
                ErrorMessage = "Error processing password reset request.";
                return null;
            }
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
                    return null;
                }

                await _repository.BeginTransactionAsync();

                if (existingOrganization != null)
                {
                    this.ErrorMessage = "Organization with the same name already exists.";
                    return null;
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
                    return null;
                }

                _logger.LogError(ex, "Error during organization registration for request: {Request}", request);
                this.ErrorMessage = "Error during organization registration.";
               
               return null;
            }
        }

        public async Task<string?> ResetPasswordAsync(ResetPasswordRequest request)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.ResetToken) || 
                    string.IsNullOrWhiteSpace(request.NewPassword))
                {
                    ErrorMessage = "Reset token and new password are required.";
                    return null;
                }

                if (request.NewPassword != request.ConfirmNewPassword)
                {
                    ErrorMessage = "New password and confirmation do not match.";
                    return null;
                }

                // Validate new password strength
                if (!IsValidPassword(request.NewPassword))
                {
                    ErrorMessage = "New password must contain at least one uppercase letter, one lowercase letter, one number, and one special character (@$!%*?&).";
                    return null;
                }

                // Find valid reset token
                var resetTokenRepo = _repository.GetRepository<PasswordResetToken>();
                var resetTokens = await resetTokenRepo.GetQuery(rt => 
                    !rt.IsUsed && 
                    rt.ExpiresAt > DateTime.UtcNow && 
                    rt.AuditProperties.IsActive && 
                    !rt.AuditProperties.IsDeleted)
                    .Include(rt => rt.User)
                    .ToListAsync();

                PasswordResetToken? validResetToken = null;
                foreach (var token in resetTokens)
                {
                    if (BCrypt.Net.BCrypt.Verify(request.ResetToken, token.TokenHash))
                    {
                        validResetToken = token;
                        break;
                    }
                }

                if (validResetToken == null)
                {
                    ErrorMessage = "Invalid or expired reset token.";
                    return null;
                }

                var user = validResetToken.User;

                // Check if user is still active
                if (!user.AuditProperties.IsActive || user.AuditProperties.IsDeleted)
                {
                    ErrorMessage = "User account is not active.";
                    return null;
                }

                // Update user password
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                user.AuditProperties.UpdatedDate = DateTime.UtcNow;
                user.AuditProperties.UpdatedBy = user.Id;

                // Mark reset token as used
                validResetToken.IsUsed = true;
                validResetToken.AuditProperties.UpdatedDate = DateTime.UtcNow;
                validResetToken.AuditProperties.UpdatedBy = user.Id;

                // Save changes
                var userRepo = _repository.GetRepository<User>();
                await userRepo.UpdateAsync(user);
                await resetTokenRepo.UpdateAsync(validResetToken);
                await _repository.SaveChangesAsync();

                // Invalidate all existing user sessions for security
                await InvalidateAllUserSessionsAsync(user.Id);

                _logger.LogInformation("Password reset successfully completed for user: {UserId}", user.Id);
                return "Password has been reset successfully. Please log in with your new password.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during password reset");
                ErrorMessage = "Error processing password reset.";
                return null;
            }
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

        /// <summary>
        /// Cleanup expired password reset tokens from the database
        /// </summary>
        public async Task CleanupExpiredPasswordResetTokensAsync()
        {
            try
            {
                var resetTokenRepo = _repository.GetRepository<PasswordResetToken>();
                var expiredTokens = await resetTokenRepo.GetQuery(rt => 
                    rt.ExpiresAt <= DateTime.UtcNow && rt.AuditProperties.IsActive)
                    .ToListAsync();

                foreach (var token in expiredTokens)
                {
                    token.AuditProperties.IsActive = false;
                    token.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    await resetTokenRepo.UpdateAsync(token);
                }

                if (expiredTokens.Any())
                {
                    await _repository.SaveChangesAsync();
                    _logger.LogInformation($"Cleaned up {expiredTokens.Count} expired password reset tokens");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired password reset tokens");
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

        /// <summary>
        /// Validates password complexity using the same pattern as the request model
        /// </summary>
        /// <param name="password">Password to validate</param>
        /// <returns>True if password meets complexity requirements</returns>
        private static bool IsValidPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                return false;

            // Pattern: ^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]+$
            // - At least one lowercase letter
            // - At least one uppercase letter  
            // - At least one digit
            // - At least one special character from @$!%*?&
            // - Only allows letters, digits, and specified special characters
            var pattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]+$";
            return System.Text.RegularExpressions.Regex.IsMatch(password, pattern);
        }

        /// <summary>
        /// Generates a cryptographically secure password reset token
        /// </summary>
        /// <returns>Password reset token</returns>
        private static string GeneratePasswordResetToken()
        {
            // Generate a 32-byte random token and convert to base64
            using var rng = RandomNumberGenerator.Create();
            var tokenBytes = new byte[32];
            rng.GetBytes(tokenBytes);
            return Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        /// <summary>
        /// Invalidates all active sessions for a user (for security after password reset)
        /// </summary>
        /// <param name="userId">User ID</param>
        private async Task InvalidateAllUserSessionsAsync(Guid userId)
        {
            try
            {
                var sessionRepo = _repository.GetRepository<UserSession>();
                var activeSessions = await sessionRepo.GetQuery(s => 
                    s.UserId == userId && 
                    s.AuditProperties.IsActive && 
                    !s.AuditProperties.IsDeleted)
                    .ToListAsync();

                foreach (var session in activeSessions)
                {
                    session.AuditProperties.IsActive = false;
                    session.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    session.AuditProperties.UpdatedBy = userId;
                    await sessionRepo.UpdateAsync(session);
                }

                if (activeSessions.Any())
                {
                    await _repository.SaveChangesAsync();
                    _logger.LogInformation("Invalidated {Count} active sessions for user: {UserId}", activeSessions.Count, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to invalidate user sessions for user: {UserId}", userId);
            }
        }
    }
}