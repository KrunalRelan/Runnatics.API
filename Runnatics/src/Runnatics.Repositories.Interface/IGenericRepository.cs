using System.Linq.Expressions;
using Runnatics.Models.Data.Common;

public interface IGenericRepository<T> where T : class
{
    Task<PagingList<T>> SearchAsync(Expression<Func<T, bool>> filter = null,
        int? pageSize = null,
        int? pageNumber = 1,
        SortDirection sortDirection = SortDirection.Ascending,
        bool ignoreQueryFilters = false,
        string? sortFieldName = null,
        bool includeNavigationProperties = false);
        
    Task<T> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<T> AddAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task<T> DeleteAsync(int id);
    Task<List<T>> AddRangeAsync(List<T> entities);
    Task<List<T>> UpdateRangeAsync(List<T> entities);
    Task DeleteRangeAsync(List<int> ids);

    IQueryable<T> GetQuery(Expression<Func<T, bool>>? filter = null,
                        bool ignoreQueryFilters = false,
                        bool includeNavigationProperties = false); 
}
