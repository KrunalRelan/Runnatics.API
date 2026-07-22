using System.Threading.Channels;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// Bounded in-process queue backed by System.Threading.Channels. Registered as a singleton.
    /// Jobs are lost on process restart (acceptable: a re-import with the opt-in ticked only
    /// re-sends to participants without a logged successful BibAssigned SMS — see the dedupe guard).
    /// </summary>
    public class BibSmsQueue : IBibSmsQueue
    {
        private readonly Channel<BibSmsJob> _channel =
            Channel.CreateBounded<BibSmsJob>(new BoundedChannelOptions(10_000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        public bool TryEnqueue(BibSmsJob job) => _channel.Writer.TryWrite(job);

        public IAsyncEnumerable<BibSmsJob> DequeueAllAsync(CancellationToken ct)
            => _channel.Reader.ReadAllAsync(ct);
    }
}
