using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runnatics.Hubs;

namespace Runnatics.Services
{
    /// <summary>
    /// DEV-ONLY mock of the RFID reader that broadcasts fake EPC tags on a timer.
    /// Replaces RfidReaderService when no physical hardware is available.
    /// Fires the same "EpcDetected" SignalR event so the React UI works identically.
    /// </summary>
    public class MockRfidReaderService : BackgroundService
    {
        private readonly IHubContext<BibMappingHub> _hubContext;
        private readonly ILogger<MockRfidReaderService> _logger;

        private const int IntervalMs = 10_000; // Fire a fake EPC every 10 seconds

        public MockRfidReaderService(
            IHubContext<BibMappingHub> hubContext,
            ILogger<MockRfidReaderService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[MockRfidReader] Started. Broadcasting fake EPCs every {Interval}s", IntervalMs / 1000);
            RfidReaderConnectionState.IsConnected = true;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(IntervalMs, stoppingToken);

                    var epc = GenerateRandomEpc();
                    var rssi = Random.Shared.Next(-80, -40);

                    await _hubContext.Clients.All.SendAsync("EpcDetected", epc, rssi, stoppingToken);

                    _logger.LogDebug("[MockRfidReader] Fired EpcDetected: EPC={Epc}, RSSI={Rssi}", epc, rssi);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[MockRfidReader] Error broadcasting fake EPC");
                }
            }

            RfidReaderConnectionState.IsConnected = false;
            _logger.LogInformation("[MockRfidReader] Stopped");
        }

        private static string GenerateRandomEpc()
        {
            var bytes = new byte[12]; // 96-bit EPC = 12 bytes
            Random.Shared.NextBytes(bytes);
            return Convert.ToHexString(bytes);
        }
    }
}
