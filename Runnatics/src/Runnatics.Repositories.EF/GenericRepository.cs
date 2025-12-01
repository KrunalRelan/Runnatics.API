namespace Runnatics.Repositories.EF
{
    using Microsoft.Data.SqlClient;
    using Microsoft.EntityFrameworkCore;
    using Runnatics.Models.Data.Common;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Text;
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
                return entity;
            }
            throw new InvalidOperationException($"Entity with id {id} not found.");
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

        public async Task<T> GetByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id) ?? throw new InvalidOperationException($"Entity with id {id} not found.");
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

        public async Task<PagingList<T>> SearchAsync(Expression<Func<T, bool>>? filter = null,
                                               int? pageSize = null,
                                               int? pageNumber = 1,
                                               SortDirection sortDirection = SortDirection.Ascending,
                                               string? sortFieldName = null,
                                               bool ignoreQueryFilters = false,
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
                var orderByExpression = BuildOrderByExpression(sortFieldName);
                query = sortDirection == SortDirection.Ascending
                    ? Queryable.OrderBy(query, (dynamic)orderByExpression)
                    : Queryable.OrderByDescending(query, (dynamic)orderByExpression);
            }

            toReturn.TotalCount = await query.CountAsync();

            if (pageSize.HasValue && pageNumber.HasValue)
            {
                query = query.Skip((pageNumber.Value - 1) * pageSize.Value).Take(pageSize.Value);
            }

            var results = await query.ToListAsync();

            toReturn.AddRange(results);

            return toReturn;
        }

        private Expression<Func<T, object>> BuildOrderByExpression(string propertyPath)
        {
            var parameter = Expression.Parameter(typeof(T), "e");
            Expression propertyAccess = parameter;

            // Split the property path by dots to handle nested properties
            var properties = propertyPath.Split('.');
            foreach (var propertyName in properties)
            {
                propertyAccess = Expression.PropertyOrField(propertyAccess, propertyName);
            }

            // Convert to object for consistent return type
            var convertedProperty = Expression.Convert(propertyAccess, typeof(object));

            return Expression.Lambda<Func<T, object>>(convertedProperty, parameter);
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

        public async Task<PagingList<T>> ExecuteStoredProcedure<I>(string procedureName, I input, string output, bool forJob = false)
        {
            var toReturn = new PagingList<T>();
            var parameters = Parameters.Transform(input, output);
            var inputParams = parameters.Where(x => x.Direction != ParameterDirection.Output).Select(x => x.ParameterName).ToList();
            var stringOfParameters = string.Empty;
            var sqlString = new StringBuilder();

            foreach (var parameter in inputParams)
            {
                sqlString.Append($"{parameter} = {parameter},");
            }

            stringOfParameters = sqlString.ToString();
            if (!string.IsNullOrEmpty(output))
            {
                stringOfParameters += "@" + output + " = @" + output + " output";
            }
            else
            {
                stringOfParameters = string.IsNullOrEmpty(stringOfParameters) ? stringOfParameters : stringOfParameters.Remove(stringOfParameters.LastIndexOf(','));
            }

            if (forJob)
            {
                context.Database.SetCommandTimeout(0);
            }

            // Use SqlQueryRaw for non-entity types
            var query = await context.Database
                .SqlQueryRaw<T>($"exec {procedureName} {stringOfParameters}", parameters.ToArray())
                .ToListAsync();

            toReturn.AddRange(query);

            if (!string.IsNullOrEmpty(output))
            {
                toReturn.TotalCount = (int)parameters[parameters.Length - 1].Value;
            }

            return toReturn;
        }

        public async Task<List<List<dynamic>>> ExecuteStoredProcedureDataSet<I>(string procedureName, I input)
        {
            List<List<dynamic>> results = new List<List<dynamic>>();
            var parameters = Parameters.Transform(input, "");
            Type responseObject = typeof(T);
            var prop = responseObject.GetProperties();
            IDbCommand? command = GetConnection(procedureName, parameters);
            Type[] typesInfo = GetClassProperties(prop, responseObject);
            int counter = 0;
            if (command != null)
            {
                using var reader = command.ExecuteReader();
                do
                {
                    var innerResults = new List<dynamic>();

                    while (reader.Read())
                    {
                        innerResults = SetValueProperties(reader, counter, typesInfo, innerResults);
                    }

                    results.Add(innerResults);
                    counter++;
                }

                while (reader.NextResult());
                reader.Close();
            }
            return results;
        }

        private List<dynamic> SetValueProperties(IDataReader reader, int counter, Type[] typesInfo, List<dynamic> innerResults)
        {

            if (counter <= typesInfo.Length - 1)
            {

                var item = Activator.CreateInstance(typesInfo[counter]);

                for (int inc = 0; inc < reader.FieldCount; inc++)
                {
                    if (item != null)
                    {

                        IterateProperties(item, reader, inc);

                    }
                }
                if (item != null)
                {
                    innerResults.Add(item);
                }
            }
            return innerResults;
        }
        private void IterateProperties(object item, IDataReader reader, int inc)
        {
            Type type = item.GetType();
            string name = reader.GetName(inc);
            PropertyInfo? property = type.GetProperty(name);

            if (property != null && name == property.Name)
            {
                var value = reader.GetValue(inc);
                if (value != null && value != DBNull.Value && !string.IsNullOrEmpty(value.ToString()))
                {
                    property.SetValue(item, Convert.ChangeType(value, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType), null);
                }
            }
        }

        private Type[] GetClassProperties(PropertyInfo[]? props, Type responseObject)
        {
            List<Type> types = new List<Type>();
            if (props != null)
            {
                foreach (var p in props)
                {
                    if (p.PropertyType.IsClass)
                    {
                        PropertyInfo? propertyInfo = responseObject.GetProperty(p.Name) ?? null;
                        if (propertyInfo != null)
                        {
                            var propType = propertyInfo.PropertyType.GetGenericArguments().Length >= 1 ?
                                           propertyInfo.PropertyType.GetGenericArguments()[0]
                                          : propertyInfo.PropertyType;
                            types.Add(propType);
                        }
                    }
                }
            }
            return types.ToArray<Type>();
        }
        private IDbCommand? GetConnection(string procedureName, SqlParameter[] parameters)
        {
            var connection = context.Database.GetDbConnection();
            var command = connection.CreateCommand();
            command.CommandText = procedureName;
            command.CommandType = CommandType.StoredProcedure;

            if (parameters != null && parameters.Any())
            {
                command.Parameters.AddRange(parameters);
            }
            if (command.Connection != null && command.Connection.State != ConnectionState.Open)
            {
                command.Connection.Open();
            }
            return command;
        }
    }
}