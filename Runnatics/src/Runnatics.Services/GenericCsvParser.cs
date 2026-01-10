using Microsoft.Extensions.Logging;
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
        public UploadFileFormat Format => UploadFileFormat.GenericCsv;

        public GenericCsvParser(ILogger<GenericCsvParser> logger)
        {
            _logger = logger;
        }

        public async Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null)
        {
            // Use same logic as ImpinjCsvParser but with configurable column names
            var impinjParser = new ImpinjCsvParser(_logger as ILogger<ImpinjCsvParser>);
            return await impinjParser.ParseAsync(stream, mapping);
        }
    }
}
