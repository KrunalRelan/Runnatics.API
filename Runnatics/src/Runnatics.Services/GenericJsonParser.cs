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
    /// Generic JSON parser
    /// </summary>
    public class GenericJsonParser : IFileParser
    {
        private readonly ILogger<GenericJsonParser> _logger;
        public UploadFileFormat Format => UploadFileFormat.CustomJson;

        public GenericJsonParser(ILogger<GenericJsonParser> logger)
        {
            _logger = logger;
        }

        public async Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null)
        {
            var impinjParser = new ImpinjJsonParser(_logger as ILogger<ImpinjJsonParser>);
            return await impinjParser.ParseAsync(stream, mapping);
        }
    }
}
