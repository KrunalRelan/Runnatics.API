using Microsoft.EntityFrameworkCore;
namespace Runnatics.Repositories.Interface
{
    public interface IUnitOfWork<C> where C : DbContext
    {
        Task SaveChangesAsync();
        IGenericRepository<T> GetRepository<T>() where T : class;
    }
}
