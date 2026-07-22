using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    /// <summary>
    /// Drains the manual completion-SMS queue and sends each on its own DI scope. Dedupe lives in
    /// NotifyCompletionSmsAsync, so re-running "Send Results SMS" never double-sends. SMS only (no email).
    /// </summary>
    public class CompletionSmsDispatcher(
        ICompletionSmsQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<CompletionSmsDispatcher> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in queue.DequeueAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var notifier = scope.ServiceProvider.GetRequiredService<IRaceNotificationService>();
                    await notifier.NotifyCompletionSmsAsync(job.ParticipantId, job.RaceId, force: false, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Completion SMS dispatch failed for participant {ParticipantId}", job.ParticipantId);
                }
            }
        }
    }
}
