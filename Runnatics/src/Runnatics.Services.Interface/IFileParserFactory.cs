using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Services.Interface
{
    public interface IFileParserFactory
    {
        public Task<IFileParser> GetParser(FileFormat format);
    }
}
