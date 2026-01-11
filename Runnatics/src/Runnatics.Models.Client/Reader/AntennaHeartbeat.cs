using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runnatics.Models.Client.Reader
{
    /// <summary>
    /// Antenna status within heartbeat
    /// </summary>
    public class AntennaHeartbeat
    {
        /// <summary>
        /// Antenna port number (1-4)
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Whether antenna is connected
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// Transmit power in centibels (cdBm)
        /// Example: 3000 = 30 dBm
        /// </summary>
        public int? TxPowerCdBm { get; set; }
    }
}
