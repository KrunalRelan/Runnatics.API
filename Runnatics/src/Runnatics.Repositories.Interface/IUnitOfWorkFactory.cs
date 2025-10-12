using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;

namespace Runnatics.Repositories.Interface
{
    public interface IUnitOfWorkFactory<C> where C : DbContext
    {
        IUnitOfWork<C> CreateUnitOfWork(string dataBaseName);
    }
}