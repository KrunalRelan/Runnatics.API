using System.Threading.Channels;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    /// <summary>Bounded in-process queue (singleton) for manual bulk completion SMS. Mirrors BibSmsQueue.</summary>
    public class CompletionSmsQueue : ICompletionSmsQueue
    {
        private readonly Channel<CompletionSmsJob> _channel =
            Channel.CreateBounded<CompletionSmsJob>(new BoundedChannelOptions(10_000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        public bool TryEnqueue(CompletionSmsJob job) => _channel.Writer.TryWrite(job);

        public IAsyncEnumerable<CompletionSmsJob> DequeueAllAsync(CancellationToken ct)
            => _channel.Reader.ReadAllAsync(ct);
    }
}
