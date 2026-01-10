using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Services.Interface
{
    public interface IFileParser
    {
        Task<List<ImpinjTagRead>> ParseAsync(Stream stream, FileUploadMapping? mapping = null);
        FileFormat Format { get; }
    }
}
