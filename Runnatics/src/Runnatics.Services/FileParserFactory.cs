using Microsoft.Extensions.DependencyInjection;
using Runnatics.Models.Data.Enumerations;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class FileParserFactory : IFileParserFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public FileParserFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task<IFileParser> GetParser(FileFormat format)
        {
            IFileParser parser = format switch
            {
                FileFormat.CSV or FileFormat.ImpinjCsv => _serviceProvider.GetRequiredService<ImpinjCsvParser>(),
                FileFormat.JSON or FileFormat.ImpinjJson => _serviceProvider.GetRequiredService<ImpinjJsonParser>(),
                FileFormat.ImpinjSqlite => _serviceProvider.GetRequiredService<ImpinjSqliteParser>(),
                FileFormat.GenericCsv or FileFormat.ChronotrackCsv => _serviceProvider.GetRequiredService<GenericCsvParser>(),
                FileFormat.CustomJson => _serviceProvider.GetRequiredService<GenericJsonParser>(),
                FileFormat.XML => throw new NotSupportedException("XML format is not yet supported"),
                _ => throw new NotSupportedException($"File format {format} is not supported")
            };
            return Task.FromResult(parser);
        }
    }
}
