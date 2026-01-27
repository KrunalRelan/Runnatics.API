using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Runnatics.Models.Client.Requests.RFID
{
    public class EPCMappingImportRequest
    {
        [Required]
        public required IFormFile File { get; set; }
    }
}
