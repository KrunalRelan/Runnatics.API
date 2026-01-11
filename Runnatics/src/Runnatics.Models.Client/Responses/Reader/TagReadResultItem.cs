using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Responses.Reader
{
    public class TagReadResultItem
    {
        public string Epc { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public bool IsDuplicate { get; set; }
        public string Error { get; set; }
    }
}
