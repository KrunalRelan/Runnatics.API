using Microsoft.EntityFrameworkCore;

namespace Runnatics.Repositories.Interface
{
    public interface IUnitOfWork<C> : IDisposable where C : DbContext
    {
        // Repository access
        IGenericRepository<T> GetRepository<T>() where T : class;
        
        // Unit of Work methods
        Task<int> SaveChangesAsync();
        
        // Transaction management
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
        
        // Multi-tenant context
        void SetTenantId(int TenantId);
        int? GetCurrentTenantId();
    }
}
