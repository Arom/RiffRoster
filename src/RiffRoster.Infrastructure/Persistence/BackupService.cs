using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace RiffRoster;

public sealed class BackupService(DataStore store, ILogger<BackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (store.CreateBackup())
                    logger.LogInformation("Created the daily RiffRoster data backup.");
            }
            catch (Exception exception)
            {
                logger.LogError("Could not create the daily RiffRoster data backup ({ErrorType}).", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
