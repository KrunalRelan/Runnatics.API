using Runnatics.Repositories.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Runnatics.Repositories.EF
{
    public class UnitOfWorkFactory<C>(IConfiguration configuration, Func<string, C> contextFactory) : IUnitOfWorkFactory<C> where C : DbContext
    {
        private readonly IConfiguration _configuration = configuration;
        private readonly Func<string, C> _contextFactory = contextFactory;

        public IUnitOfWork<C> CreateUnitOfWork(string dataBaseName)
        {
            ArgumentException.ThrowIfNullOrEmpty(dataBaseName, nameof(dataBaseName));
            var connectionString = _configuration.GetConnectionString("dataBaseName");
            var options = new DbContextOptionsBuilder<C>()
                .UseSqlServer(connectionString)
                .Options;
            var context = _contextFactory(dataBaseName);

            return new UnitOfWork<C>(context);
        }
    }
}