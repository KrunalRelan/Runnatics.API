namespace Runnatics.Services.Interface
{
    /// <summary>
    /// Provides access to the current authenticated user's context information
    /// </summary>
    public interface IUserContextService
    {
        /// <summary>
        /// Gets the current user's ID from the JWT token
        /// </summary>
        int UserId { get; }

        /// <summary>
        /// Gets the current user's organization ID from the JWT token
        /// </summary>
        int TenantId { get; }

        /// <summary>
        /// Gets the current user's email from the JWT token
        /// </summary>
        string Email { get; }

        /// <summary>
        /// Gets the current user's role from the JWT token
        /// </summary>
        string Role { get; }

        /// <summary>
        /// Gets the current user's full name from the JWT token
        /// </summary>
        string FullName { get; }

        /// <summary>
        /// Checks if the user is authenticated
        /// </summary>
        bool IsAuthenticated { get; }
    }
}
