namespace Orders.FulfillmentJob;

public sealed class Worker(FulfillmentRunner runner, IHostApplicationLifetime lifetime, ILogger<Worker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var handled = await runner.RunAsync(stoppingToken);
            log.LogInformation("Fulfillment run complete, {Count} orders handled", handled);
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
