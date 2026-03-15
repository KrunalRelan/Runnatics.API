using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Runnatics.Hubs;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// DEV-ONLY controller that simulates RFID reader EPC detections via SignalR
    /// without needing physical hardware. Fires the same "EpcDetected" event
    /// that the real RfidReaderService would.
    /// </summary>
    [ApiController]
    [Route("api/simulator")]
    [Produces("application/json")]
    public class RfidSimulatorController(
        IHubContext<BibMappingHub> hubContext,
        ILogger<RfidSimulatorController> logger) : ControllerBase
    {
        private readonly IHubContext<BibMappingHub> _hubContext = hubContext;
        private readonly ILogger<RfidSimulatorController> _logger = logger;

        /// <summary>
        /// Simulate a specific EPC tag detection. Fires "EpcDetected" through BibMappingHub
        /// exactly as the real GReaderApi reader would.
        /// </summary>
        [HttpPost("detect-epc")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SimulateEpcDetected(
            [FromBody] SimulateEpcRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Epc))
                return BadRequest(new { error = "EPC is required." });

            await _hubContext.Clients.All.SendAsync("EpcDetected", request.Epc, request.Rssi, cancellationToken);

            _logger.LogInformation("[Simulator] Fired EpcDetected: EPC={Epc}, RSSI={Rssi}", request.Epc, request.Rssi);

            return Ok(new { message = $"Simulated EPC: {request.Epc}", epc = request.Epc, rssi = request.Rssi });
        }

        /// <summary>
        /// Generate and broadcast a random realistic-looking EPC tag.
        /// Useful for rapid testing without typing hex strings.
        /// </summary>
        [HttpPost("detect-random")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> SimulateRandomEpc(CancellationToken cancellationToken)
        {
            var epc = GenerateRandomEpc();
            var rssi = Random.Shared.Next(-80, -40);

            await _hubContext.Clients.All.SendAsync("EpcDetected", epc, rssi, cancellationToken);

            _logger.LogInformation("[Simulator] Fired random EpcDetected: EPC={Epc}, RSSI={Rssi}", epc, rssi);

            return Ok(new { epc, rssi });
        }

        /// <summary>
        /// Fire a batch of random EPCs in rapid succession.
        /// Simulates multiple chips being scanned quickly.
        /// </summary>
        [HttpPost("detect-batch")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> SimulateBatch(
            [FromQuery] int count = 5,
            [FromQuery] int delayMs = 500,
            CancellationToken cancellationToken = default)
        {
            if (count is < 1 or > 50)
                return BadRequest(new { error = "Count must be between 1 and 50." });

            var results = new List<object>();

            for (int i = 0; i < count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var epc = GenerateRandomEpc();
                var rssi = Random.Shared.Next(-80, -40);

                await _hubContext.Clients.All.SendAsync("EpcDetected", epc, rssi, cancellationToken);
                results.Add(new { epc, rssi });

                if (i < count - 1 && delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken);
            }

            _logger.LogInformation("[Simulator] Fired batch of {Count} EPC detections", results.Count);

            return Ok(new { count = results.Count, detections = results });
        }

        /// <summary>
        /// Check the real reader connection state (from RfidReaderConnectionState).
        /// </summary>
        [HttpGet("reader-status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetReaderStatus()
        {
            return Ok(new
            {
                isConnected = RfidReaderConnectionState.IsConnected,
                mode = RfidReaderConnectionState.IsConnected ? "hardware" : "simulator"
            });
        }

        private static string GenerateRandomEpc()
        {
            var bytes = new byte[12]; // 96-bit EPC = 12 bytes
            Random.Shared.NextBytes(bytes);
            return Convert.ToHexString(bytes);
        }
    }

    public class SimulateEpcRequest
    {
        public string Epc { get; set; } = string.Empty;
        public int Rssi { get; set; } = -65;
    }
}
