using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Services.Interface
{
    public interface IFileParser
    {
        Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null);
        UploadFileFormat Format { get; }
    }
}
