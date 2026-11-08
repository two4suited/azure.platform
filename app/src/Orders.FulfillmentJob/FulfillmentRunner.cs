using System.Text.Json;
using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Microsoft.Azure.Cosmos;
using Orders.Contracts;

namespace Orders.FulfillmentJob;

/// <summary>
/// Drains the fulfillment subscription: marks each order shipped, records an audit row,
/// and asks for an invoice. The audit subscription is drained the same way.
/// </summary>
public sealed class FulfillmentRunner(
    ServiceBusClient bus, CosmosClient cosmos, TableServiceClient tables, QueueServiceClient queues,
    ILogger<FulfillmentRunner> log)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var audit = tables.GetTableClient(Messaging.AuditTable);
        await audit.CreateIfNotExistsAsync(ct);
        var invoices = queues.GetQueueClient(Messaging.InvoiceQueue);
        var orders = cosmos.GetContainer(CosmosNames.Database, CosmosNames.OrdersContainer);

        var handled = 0;
        await using var receiver = bus.CreateReceiver(Messaging.OrderEventsTopic, Messaging.FulfillmentSubscription);
        while (!ct.IsCancellationRequested)
        {
            var messages = await receiver.ReceiveMessagesAsync(50, TimeSpan.FromSeconds(5), ct);
            if (messages.Count == 0) break;

            foreach (var message in messages)
            {
                var evt = JsonSerializer.Deserialize<OrderPlaced>(message.Body)!;
                var order = (await orders.ReadItemAsync<Order>(evt.OrderId, new PartitionKey(evt.OrderId), cancellationToken: ct)).Resource;
                await orders.ReplaceItemAsync(order with { Status = "Shipped", ShippedAt = DateTimeOffset.UtcNow },
                    order.Id, new PartitionKey(order.Id), cancellationToken: ct);

                await audit.AddEntityAsync(new TableEntity("orders", $"{evt.OrderId}-shipped")
                {
                    ["Total"] = (double)evt.Total,
                    ["Customer"] = evt.CustomerEmail,
                    ["At"] = DateTimeOffset.UtcNow,
                }, ct);
                await invoices.SendMessageAsync(
                    BinaryData.FromObjectAsJson(new InvoiceRequest(evt.OrderId, evt.CustomerEmail, evt.Total)), cancellationToken: ct);

                await receiver.CompleteMessageAsync(message, ct);
                handled++;
                log.LogInformation("Shipped order {OrderId}", evt.OrderId);
            }
        }
        return handled;
    }
}
