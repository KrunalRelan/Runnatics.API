using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Reader
{
    /// <summary>
    /// Response for reader registration
    /// </summary>
    public class ReaderRegistrationResponse
    {
        public bool Success { get; set; }
        public int ReaderId { get; set; }
        public string Message { get; set; }
        public ReaderConfigResponse Config { get; set; }
    }
}
