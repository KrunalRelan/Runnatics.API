namespace Runnatics.Data.EF
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    using Microsoft.EntityFrameworkCore;

    public static class FilterExtension 
    {
       public static ModelBuilder DefaultFilters(this ModelBuilder modelBuilder)
       {
           // Apply global query filters here
           foreach (var entityType in modelBuilder.Model.GetEntityTypes())
           {
               // Check if the entity has an IsDeleted property
               var auditPropertiesNavigation = entityType.FindNavigation("AuditProperties");
               if (auditPropertiesNavigation != null)
               {
                   var auditPropertiesType = auditPropertiesNavigation.ClrType;
                   var isActiveProperty = auditPropertiesType.GetProperties().FirstOrDefault(p => p.Name == "IsActive");
                   var isDeletedProperty = auditPropertiesType.GetProperties().FirstOrDefault(p => p.Name == "IsDeleted");

                   if (isActiveProperty != null && isDeletedProperty != null)
                   {
                        var parameter = Expression.Parameter(entityType.ClrType, "e");

                       var auditPropertiesAccess = Expression.Property(parameter, property: auditPropertiesNavigation.PropertyInfo);
                       var isActive = Expression.Property(auditPropertiesAccess, isActiveProperty);
                        var isNotDeleted = Expression.Not(Expression.Property(auditPropertiesAccess, isDeletedProperty));
                       var predicateBody = Expression.AndAlso(
                           Expression.Equal(isActive, Expression.Constant(true)),
                           Expression.Equal(isNotDeleted, Expression.Constant(false))
                       );
                       var compareExpression = Expression.Equal(isNotDeleted, Expression.Constant(false));
                       var lambda = Expression.Lambda(predicateBody, parameter);

                       modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
                   }
               }
           }
           return modelBuilder;
       }
    }
}