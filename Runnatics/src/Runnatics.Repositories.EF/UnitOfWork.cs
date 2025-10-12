using Microsoft.EntityFrameworkCore;
using Runnatics.Repositories.Interface;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace Runnatics.Repositories.EF
{
    public class UnitOfWork<C> : IUnitOfWork<C> where C : DbContext
    {
        private readonly C _context;

        public UnitOfWork(C context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }
       
        public void Dispose()
        {
            _context.Dispose();
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }

        public IGenericRepository<T> GetRepository<T>() where T : class
        {
            return new GenericRepository<T>(_context);
        }
    }
}