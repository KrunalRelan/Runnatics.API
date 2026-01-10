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
    /// Generic JSON parser
    /// </summary>
    public class GenericJsonParser : IFileParser
    {
        private readonly ILogger<GenericJsonParser> _logger;
        public FileFormat Format => FileFormat.JSON;

        public GenericJsonParser(ILogger<GenericJsonParser> logger)
        {
            _logger = logger;
        }

        public async Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null)
        {
            var impinjParser = new ImpinjJsonParser(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ImpinjJsonParser>.Instance);
            return await impinjParser.ParseAsync(stream, mapping);
        }
    }
}
