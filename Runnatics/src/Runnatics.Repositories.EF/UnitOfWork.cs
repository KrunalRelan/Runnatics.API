using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Runnatics.Repositories.Interface;
using System.Threading.Tasks;

namespace Runnatics.Repositories.EF
{
    /// <summary>
    /// Unit of Work pattern implementation with transaction management and multi-tenant support
    /// </summary>
    /// <typeparam name="C">DbContext type</typeparam>
    /// <example>
    /// Usage example:
    /// <code>
    /// // Basic usage with transaction
    /// await _unitOfWork.BeginTransactionAsync();
    /// try
    /// {
    ///     var eventRepo = _unitOfWork.GetRepository&lt;Event&gt;();
    ///     await eventRepo.AddAsync(newEvent);
    ///     await _unitOfWork.SaveChangesAsync();
    ///     await _unitOfWork.CommitTransactionAsync();
    /// }
    /// catch
    /// {
    ///     await _unitOfWork.RollbackTransactionAsync();
    ///     throw;
    /// }
    /// 
    /// // Multi-tenant usage
    /// _unitOfWork.SetTenantId(organizationId);
    /// var currentTenant = _unitOfWork.GetCurrentTenantId();
    /// </code>
    /// </example>
    public class UnitOfWork<C> : IUnitOfWork<C> where C : DbContext
    {
        private readonly C _context;
        private IDbContextTransaction? _transaction;
        private int? _tenantId;

        public UnitOfWork(C context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }
       
        public void Dispose()
        {
            _transaction?.Dispose();
            _context.Dispose();
        }

        /// <summary>
        /// Saves all changes made in this context to the database asynchronously.
        /// </summary>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Gets the repository instance for the specified entity type.
        /// </summary>
        /// <typeparam name="T">The entity type</typeparam>
        /// <returns>The repository instance</returns>
        public IGenericRepository<T> GetRepository<T>() where T : class
        {
            return new GenericRepository<T>(_context);
        }

        // Transaction management
        /// <summary>
        /// Begins a new transaction asynchronously.
        /// </summary>
        /// <exception cref="InvalidOperationException">A transaction is already in progress.</exception>
        public async Task BeginTransactionAsync()
        {
            if (_transaction != null)
            {
                throw new InvalidOperationException("A transaction is already in progress.");
            }

            _transaction = await _context.Database.BeginTransactionAsync();
        }

        /// <summary>
        /// Commits the current transaction asynchronously.
        /// </summary>
        /// <exception cref="InvalidOperationException">No transaction in progress to commit.</exception>
        public async Task CommitTransactionAsync()
        {
            if (_transaction == null)
            {
                throw new InvalidOperationException("No transaction in progress to commit.");
            }

            try
            {
                await _context.SaveChangesAsync();
                await _transaction.CommitAsync();
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

        /// <summary>
        /// Rolls back the current transaction asynchronously.
        /// </summary>
        /// <exception cref="InvalidOperationException">No transaction in progress to rollback.</exception>
        public async Task RollbackTransactionAsync()
        {
            if (_transaction == null)
            {
                throw new InvalidOperationException("No transaction in progress to rollback.");
            }

            try
            {
                await _transaction.RollbackAsync();
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

        // Multi-tenant context
        /// <summary>
        /// Sets the tenant ID for the current context.
        /// </summary>
        /// <param name="organizationId">The organization ID (tenant ID)</param>
        public void SetTenantId(int organizationId)
        {
            _tenantId = organizationId;
        }

        /// <summary>
        /// Gets the current tenant ID.
        /// </summary>
        /// <returns>The current tenant ID, or null if not set</returns>
        public int? GetCurrentTenantId()
        {
            return _tenantId;
        }
    }
}