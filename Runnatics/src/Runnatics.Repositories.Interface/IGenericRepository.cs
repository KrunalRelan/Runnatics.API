using Microsoft.EntityFrameworkCore;
using Runnatics.Models.Data.Common;
using System.Linq.Expressions;

public interface IGenericRepository<T> where T : class
{
    Task<PagingList<T>> SearchAsync(Expression<Func<T, bool>> filter = null,
        int? pageSize = null,
        int? pageNumber = 1,
        SortDirection sortDirection = SortDirection.Ascending,
        string? sortFieldName = null,
        bool ignoreQueryFilters = false,
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

    public Task<PagingList<T>> ExecuteStoredProcedure<I>(string procedureName, I input, string output, bool forJob = false);

    public Task<List<List<dynamic>>> ExecuteStoredProcedureDataSet<I>(string procedureName, I input);

    public Task<int> CountAsync(Expression<Func<T, bool>>? filter = null, bool ignoreQueryFilters = false);
}
