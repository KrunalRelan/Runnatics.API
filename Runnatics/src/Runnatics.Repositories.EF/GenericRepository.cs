namespace Runnatics.Repositories.EF
{
    using Microsoft.EntityFrameworkCore;
    using Runnatics.Models.Data.Common;
    using Runnatics.Repositories.Interface;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading.Tasks;

    public class GenericRepository<T>(DbContext context) : IGenericRepository<T> where T : class
    {
        private readonly DbSet<T> _dbSet = context.Set<T>();
        internal DbContext context = context;

        public async Task<T> AddAsync(T entity)
        {
            await _dbSet.AddAsync(entity);
            
            return entity;
        }

        public async Task<List<T>> AddRangeAsync(List<T> entities)
        {
            await _dbSet.AddRangeAsync(entities);
            return entities;
        }

        public async Task<T> DeleteAsync(int id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity != null)
            {
                _dbSet.Remove(entity);
            }
            return entity;
        }

        public async Task DeleteRangeAsync(List<int> ids)
        {
            var keyProperty = context.Model.FindEntityType(typeof(T))?.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (keyProperty == null)
                throw new InvalidOperationException("No primary key defined for entity.");

            var entities = await _dbSet.Where(e =>
                ids.Contains((int)typeof(T).GetProperty(keyProperty.Name)!.GetValue(e)!)).ToListAsync();
            _dbSet.RemoveRange(entities);
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }

        public async Task<T?> GetByIdAsync(Expression<Func<T, bool>> filter)
        {
            return await _dbSet.FirstOrDefaultAsync(filter);
        }

        public async Task<T?> GetByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);
        }

        public IQueryable<T> GetQuery(Expression<Func<T, bool>>? filter = null,
                                        bool ignoreQueryFilters = false,
                                        bool includeNavigationProperties = false)
        {
            IQueryable<T> query = ignoreQueryFilters ? _dbSet.IgnoreQueryFilters() : _dbSet;

            if (filter != null)
            {
                query = query.Where(filter);
            }
            if (includeNavigationProperties)
            {
                var navigationProperties = context.Model.FindEntityType(typeof(T))?.GetNavigations();
                if (navigationProperties != null)
                {
                    foreach (var navigationProperty in navigationProperties)
                    {
                        query = query.Include(navigationProperty.Name);
                    }
                }
            }          

            return query;
        }

        public async Task<PagingList<T>> SearchAsync(Expression<Func<T, bool>> filter = null,
                                               int? pageSize = null,
                                               int? pageNumber = 1,
                                               SortDirection sortDirection = SortDirection.Ascending,
                                               bool ignoreQueryFilters = false,
                                               string? sortFieldName = null,
                                               bool includeNavigationProperties = false)
        {
            var toReturn = new PagingList<T>();

            IQueryable<T> query = _dbSet;
            if (includeNavigationProperties)
            {
                var navigationProperties = context.Model.FindEntityType(typeof(T))?.GetNavigations();
                if (navigationProperties != null)
                {
                    foreach (var navigationProperty in navigationProperties)
                    {
                        query = query.Include(navigationProperty.Name);
                    }
                }
            }
            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (sortFieldName != null)
            {
                query = sortDirection == SortDirection.Ascending
                    ? query.OrderBy(e => EF.Property<object>(e, sortFieldName))
                    : query.OrderByDescending(e => EF.Property<object>(e, sortFieldName));
            }
            
            toReturn.TotalCount = query.Count();

            if (pageSize.HasValue && pageNumber.HasValue)
            {
                query = query.Skip((pageNumber.Value - 1) * pageSize.Value).Take(pageSize.Value);
            }

            var results = await query.ToListAsync();

            toReturn.AddRange(results);

            return toReturn;
        }

        public async Task<T> UpdateAsync(T entity)
        {
            var result = _dbSet.Update(entity);
            context.Entry(entity).State = EntityState.Modified;

            return await Task.FromResult(result.Entity);
        }

        public async Task<List<T>> UpdateRangeAsync(List<T> entities)
        {
            _dbSet.UpdateRange(entities);

            return await Task.FromResult(entities);
        }
    }
}