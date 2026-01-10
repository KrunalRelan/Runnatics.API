using Microsoft.Extensions.DependencyInjection;
using Runnatics.Services.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services
{
    public class FileParserFactory : IFileParserFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public FileParserFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IFileParser GetParser(UploadFileFormat format)
        {
            return format switch
            {
                UploadFileFormat.ImpinjCsv => _serviceProvider.GetRequiredService<ImpinjCsvParser>(),
                UploadFileFormat.ImpinjJson => _serviceProvider.GetRequiredService<ImpinjJsonParser>(),
                UploadFileFormat.GenericCsv => _serviceProvider.GetRequiredService<GenericCsvParser>(),
                UploadFileFormat.CustomJson => _serviceProvider.GetRequiredService<GenericJsonParser>(),
                _ => throw new NotSupportedException($"File format {format} is not supported")
            };
        }
    }
}
