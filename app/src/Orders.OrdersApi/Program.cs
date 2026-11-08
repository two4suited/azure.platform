using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using Orders.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddAzureCosmosClient("cosmos");
builder.AddAzureServiceBusClient("messaging");
builder.Services.AddHttpClient("catalog", c => c.BaseAddress = new Uri("https+http://catalog-api"));
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOpenApi();

static Container Orders(CosmosClient c) =>
    c.GetContainer(CosmosNames.Database, CosmosNames.OrdersContainer);

app.MapPost("/orders", async (PlaceOrderRequest request, CosmosClient cosmos, ServiceBusClient bus,
    IHttpClientFactory http, CancellationToken ct) =>
{
    if (request.Lines.Count == 0) return Results.BadRequest("An order needs at least one line.");

    // Price every line from the catalog rather than trusting the client.
    var catalog = http.CreateClient("catalog");
    var lines = new List<OrderLine>();
    foreach (var line in request.Lines)
    {
        var product = await catalog.GetFromJsonAsync<Product>($"/products/{line.ProductId}", ct);
        if (product is null) return Results.BadRequest($"Unknown product {line.ProductId}.");
        lines.Add(line with { UnitPrice = product.Price });
    }

    var order = new Order(Guid.NewGuid().ToString("n"), request.CustomerEmail, lines,
        lines.Sum(l => l.Quantity * l.UnitPrice), "Placed", DateTimeOffset.UtcNow);
    await Orders(cosmos).CreateItemAsync(order, new PartitionKey(order.Id), cancellationToken: ct);

    await using var sender = bus.CreateSender(Messaging.OrderEventsTopic);
    var evt = new OrderPlaced(order.Id, order.CustomerEmail, order.Total, order.PlacedAt);
    await sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(evt))
    {
        Subject = nameof(OrderPlaced),
        ContentType = "application/json",
    }, ct);

    return Results.Created($"/orders/{order.Id}", order);
}).WithName("PlaceOrder");

app.MapGet("/orders/{id}", async (string id, CosmosClient cosmos) =>
{
    try { return Results.Ok((await Orders(cosmos).ReadItemAsync<Order>(id, new PartitionKey(id))).Resource); }
    catch (CosmosException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return Results.NotFound(); }
}).WithName("GetOrder");

app.Run();
