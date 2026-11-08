using Azure.Messaging.ServiceBus;

namespace Orders.FulfillmentJob;

public sealed class Worker(FulfillmentRunner runner, IHostApplicationLifetime lifetime, ILogger<Worker> log)
    : BackgroundService
{
    // Locally the Service Bus emulator can still be starting when the job launches,
    // so retry connection failures for a while before giving up.
    private static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var deadline = DateTime.UtcNow + RetryWindow;
            while (true)
            {
                try
                {
                    var handled = await runner.RunAsync(stoppingToken);
                    log.LogInformation("Fulfillment run complete, {Count} orders handled", handled);
                    break;
                }
                catch (ServiceBusException ex) when (
                    ex.Reason == ServiceBusFailureReason.ServiceCommunicationProblem && DateTime.UtcNow < deadline)
                {
                    log.LogWarning("Service Bus not reachable yet, retrying in {Delay}s", RetryDelay.TotalSeconds);
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
