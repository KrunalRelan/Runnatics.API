using Microsoft.EntityFrameworkCore;
using Runnatics.Models.Data.Common;

namespace Runnatics.Repositories.Interface
{
    public interface IUnitOfWork<C> : IDisposable where C : DbContext
    {
        // Repository access
        IGenericRepository<T> GetRepository<T>() where T : class;

        // Unit of Work methods
        Task<int> SaveChangesAsync();

        // Remove an entity from the change tracker (e.g. after a failed insert so the outer
        // SaveChanges will not retry it). No SQL is emitted.
        void Detach<T>(T entity) where T : class;

        // Transaction management
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
        Task ExecuteInTransactionAsync(Func<Task> operation);

        // Multi-tenant context
        void SetTenantId(int TenantId);
        int? GetCurrentTenantId();

        Task<PagingList<O>> ExecuteStoredProcedure<I, O>(string storedProcedureName, I input, string? output = null) where O: class;
    }
}
