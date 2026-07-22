using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// Drains the in-process BIB-SMS queue and sends each on its own DI scope. The dedupe guard
    /// lives inside NotifyBibAssignedAsync, so a re-import never re-blasts already-notified runners.
    /// </summary>
    public class BibSmsDispatcher(
        IBibSmsQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BibSmsDispatcher> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in queue.DequeueAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var notifier = scope.ServiceProvider.GetRequiredService<IRaceNotificationService>();
                    await notifier.NotifyBibAssignedAsync(job.ParticipantId, job.RaceId, force: false, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "BIB SMS dispatch failed for participant {ParticipantId}", job.ParticipantId);
                }
            }
        }
    }
}
