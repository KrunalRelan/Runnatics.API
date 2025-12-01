using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Repositories.EF
{
    public static class Parameters
    {
        public static SqlParameter[] Transform<I>(I input, string output)
        {
            List<SqlParameter> parameters = [];
            if (input is not null)
            {
             var properties = input.GetType().GetProperties();
                foreach (var property in properties)
                {
                    var value = property.GetValue(input) ?? DBNull.Value;
                    parameters.Add(new SqlParameter($"@{property.Name}", value));
                }
            }
            if (!string.IsNullOrEmpty(output))
            {
                var outputParam = new SqlParameter($"@{output}", System.Data.SqlDbType.Int)
                {
                    Direction = System.Data.ParameterDirection.Output
                };
                parameters.Add(outputParam);
            }
        }
    }
}
