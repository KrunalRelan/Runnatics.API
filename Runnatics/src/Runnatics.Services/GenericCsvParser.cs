using Microsoft.Extensions.Logging;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Services.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services
{
    /// <summary>
    /// Generic CSV parser with configurable columns
    /// </summary>
    public class GenericCsvParser : IFileParser
    {
        private readonly ILogger<GenericCsvParser> _logger;
        public FileFormat Format => FileFormat.CSV;

        public GenericCsvParser(ILogger<GenericCsvParser> logger)
        {
            _logger = logger;
        }

        public async Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null)
        {
            // Use same logic as ImpinjCsvParser
            var impinjParser = new ImpinjCsvParser(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ImpinjCsvParser>.Instance);
            return await impinjParser.ParseAsync(stream, mapping);
        }
    }
}
