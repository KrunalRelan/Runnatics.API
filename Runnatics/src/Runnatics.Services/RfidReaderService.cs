using System.Threading.Channels;
using GDotnet.Reader.Api.DAL;
using GDotnet.Reader.Api.Protocol.Gx;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runnatics.Hubs;

namespace Runnatics.Services
{
    /// <summary>
    /// Background service that connects to a physical RFID reader via TCP (GReaderApi SDK),
    /// reads EPC tags continuously, and broadcasts them to the BibMappingHub via SignalR.
    /// </summary>
    public class RfidReaderService : BackgroundService
    {
        private readonly IHubContext<BibMappingHub> _hubContext;
        private readonly ILogger<RfidReaderService> _logger;
        private Channel<(string Epc, int Rssi)> _tagChannel;

        private GClient? _clientConn;
        private const string ReaderAddress = "192.168.1.168:8160";
        private const int ConnectTimeoutMs = 3000;
        private const int ReconnectDelayMs = 5000;

        private volatile bool _stopping;

        public RfidReaderService(
            IHubContext<BibMappingHub> hubContext,
            ILogger<RfidReaderService> logger,
            IConfiguration configuration)
        {
            _hubContext = hubContext;
            _logger = logger;
            _debounceMs = configuration.GetValue("R700Settings:DebounceMs", 2000);

            _tagChannel = CreateChannel();
        }

        private static Channel<(string, int)> CreateChannel()
        {
            // Unbounded channel — reader callbacks must never block
            return Channel.CreateUnbounded<(string, int)>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });
        }

        // Pending reads accumulator for the RSSI debounce window: EPC → best RSSI seen so far.
        // Window length comes from config (R700Settings:DebounceMs, default 2000).
        private readonly Dictionary<string, int> _pendingReads = [];
        private CancellationTokenSource? _debounceTokenSource;
        private readonly SemaphoreSlim _debounceLock = new(1, 1);
        private readonly int _debounceMs;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.Register(() => _stopping = true);

            // Connect (with retry) then start processing loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Create a fresh channel for each connection cycle
                    _tagChannel = CreateChannel();

                    ConnectAndStartInventory();

                    // Process tags from channel with the configured RSSI debounce window
                    await foreach (var (epc, rssi) in _tagChannel.Reader.ReadAllAsync(stoppingToken))
                    {
                        await AccumulateAndDebounceAsync(epc, rssi, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RFID reader connection lost. Reconnecting in {Delay}ms...", ReconnectDelayMs);
                    RfidReaderConnectionState.IsConnected = false;
                    CleanupConnection();

                    try
                    {
                        await Task.Delay(ReconnectDelayMs, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Accumulates EPC reads for the configured debounce window and broadcasts the best
        /// RSSI read per EPC. If the same EPC arrives again before the window fires, its RSSI
        /// is updated if higher.
        /// </summary>
        private async Task AccumulateAndDebounceAsync(string epc, int rssi, CancellationToken stoppingToken)
        {
            await _debounceLock.WaitAsync(stoppingToken);
            try
            {
                // Keep highest RSSI seen for each EPC in the current window
                if (!_pendingReads.TryGetValue(epc, out var existingRssi) || rssi > existingRssi)
                    _pendingReads[epc] = rssi;

                // Reset the debounce timer
                _debounceTokenSource?.Cancel();
                _debounceTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var debounceToken = _debounceTokenSource.Token;

                // Fire-and-forget the flush task so we don't hold the lock during the delay
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(_debounceMs, debounceToken);

                        // Flush window — grab all accumulated reads
                        Dictionary<string, int> batch;
                        await _debounceLock.WaitAsync(stoppingToken);
                        try
                        {
                            batch = new Dictionary<string, int>(_pendingReads);
                            _pendingReads.Clear();
                        }
                        finally
                        {
                            _debounceLock.Release();
                        }

                        // Multiple tags in one debounce window → single event so the UI can reject the batch
                        if (batch.Count > 1)
                        {
                            var epcs = batch.Keys.ToArray();
                            try
                            {
                                await _hubContext.Clients.All.SendAsync("MultipleEpcDetected", epcs, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to broadcast MultipleEpcDetected ({Count} EPCs) via SignalR", epcs.Length);
                            }
                        }
                        else
                        {
                            var (batchEpc, batchRssi) = batch.First();
                            try
                            {
                                await _hubContext.Clients.All.SendAsync("EpcDetected", batchEpc, batchRssi, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to broadcast EPC={Epc} via SignalR", batchEpc);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // A new read arrived before the window expired — normal debounce reset
                    }
                }, stoppingToken);
            }
            finally
            {
                _debounceLock.Release();
            }
        }

        private void ConnectAndStartInventory()
        {
            _logger.LogInformation("Connecting to RFID reader at {Address}...", ReaderAddress);

            _clientConn = new GClient();

            // Subscribe to disconnect event for reconnection
            // SDK delegate: void delegateTcpDisconnected(string readerName)
            _clientConn.OnTcpDisconnected += OnTcpDisconnected;

            // Connect via TCP — SDK requires out status param
            eConnectionAttemptEventStatusType status;
            var connected = _clientConn.OpenTcp(ReaderAddress, ConnectTimeoutMs, out status);
            if (!connected)
            {
                throw new InvalidOperationException(
                    $"Failed to connect to RFID reader at {ReaderAddress}. Status={status}");
            }

            _logger.LogInformation("Connected to RFID reader at {Address}, Status={Status}", ReaderAddress, status);
            RfidReaderConnectionState.IsConnected = true;

            // Always stop any existing inventory before configuring
            var stopMsg = new MsgBaseStop();
            _clientConn.SendSynMsg(stopMsg);

            // Subscribe to EPC tag events — callback must not block
            // SDK delegate: void delegateEncapedTagEpcLog(EncapedLogBaseEpcInfo msg)
            _clientConn.OnEncapedTagEpcLog += OnTagDetected;

            // Start continuous inventory on Antenna 1
            // AntennaEnable is UInt32 (bitmask), eAntennaNo._1 = 1
            // InventoryMode is Byte, eInventoryMode.Inventory = 1
            var inventoryMsg = new MsgBaseInventoryEpc
            {
                AntennaEnable = eAntennaNo._1,
                InventoryMode = (byte)eInventoryMode.Inventory
            };

            _clientConn.SendSynMsg(inventoryMsg);

            if (inventoryMsg.RtCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to start inventory. RtCode={inventoryMsg.RtCode}, RtMsg={inventoryMsg.RtMsg}");
            }

            _logger.LogInformation("RFID inventory started in continuous mode on Antenna 1");
        }

        /// <summary>
        /// SDK callback: delegateEncapedTagEpcLog — single param, no sender.
        /// MUST NOT block — writes to Channel for async processing.
        /// </summary>
        private void OnTagDetected(EncapedLogBaseEpcInfo msg)
        {
            // Only process successful reads
            if (msg.logBaseEpcInfo.Result != 0)
                return;

            var epc = msg.logBaseEpcInfo.Epc;
            var rssi = (int)msg.logBaseEpcInfo.Rssi;

            if (string.IsNullOrWhiteSpace(epc))
                return;

            // Non-blocking write to channel — never block the SDK callback thread
            if (!_tagChannel.Writer.TryWrite((epc, rssi)))
            {
                _logger.LogWarning("Tag channel full, dropping EPC={Epc}", epc);
            }
        }

        /// <summary>
        /// SDK callback: delegateTcpDisconnected — single string param (readerName).
        /// </summary>
        private void OnTcpDisconnected(string readerName)
        {
            _logger.LogWarning("RFID reader TCP connection lost: {ReaderName}", readerName);
            RfidReaderConnectionState.IsConnected = false;

            if (!_stopping)
            {
                // Complete the channel to exit the ReadAllAsync loop,
                // which will trigger reconnection in ExecuteAsync
                _tagChannel.Writer.TryComplete();
            }
        }

        private void CleanupConnection()
        {
            if (_clientConn != null)
            {
                try
                {
                    _clientConn.OnEncapedTagEpcLog -= OnTagDetected;
                    _clientConn.OnTcpDisconnected -= OnTcpDisconnected;
                    _clientConn.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during RFID reader cleanup");
                }

                _clientConn = null;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _stopping = true;
            _logger.LogInformation("Stopping RFID reader service...");

            if (_clientConn != null)
            {
                try
                {
                    // Stop inventory
                    var stopMsg = new MsgBaseStop();
                    _clientConn.SendSynMsg(stopMsg);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error sending stop command");
                }

                CleanupConnection();
            }

            RfidReaderConnectionState.IsConnected = false;
            _tagChannel.Writer.TryComplete();

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("RFID reader service stopped");
        }
    }
}
