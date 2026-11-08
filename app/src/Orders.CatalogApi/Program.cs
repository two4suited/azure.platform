using Microsoft.Azure.Cosmos;
using Orders.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddAzureCosmosClient("cosmos");
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOpenApi();

static Container Products(CosmosClient c) =>
    c.GetContainer(CosmosNames.Database, CosmosNames.ProductsContainer);

app.MapGet("/products", async (CosmosClient cosmos) =>
{
    var products = new List<Product>();
    using var it = Products(cosmos).GetItemQueryIterator<Product>("SELECT * FROM c");
    while (it.HasMoreResults) products.AddRange(await it.ReadNextAsync());
    return Results.Ok(products);
}).WithName("ListProducts");

app.MapGet("/products/{id}", async (string id, CosmosClient cosmos) =>
{
    try { return Results.Ok((await Products(cosmos).ReadItemAsync<Product>(id, new PartitionKey(id))).Resource); }
    catch (CosmosException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return Results.NotFound(); }
}).WithName("GetProduct");

app.MapPost("/products", async (Product product, CosmosClient cosmos) =>
{
    await Products(cosmos).UpsertItemAsync(product, new PartitionKey(product.Id));
    return Results.Created($"/products/{product.Id}", product);
}).WithName("UpsertProduct");

// Seed a few products the first time the container is empty.
_ = Task.Run(async () =>
{
    try
    {
        var container = Products(app.Services.GetRequiredService<CosmosClient>());
        using var it = container.GetItemQueryIterator<int>("SELECT VALUE COUNT(1) FROM c");
        if ((await it.ReadNextAsync()).FirstOrDefault() > 0) return;
        Product[] seed =
        [
            new("widget", "Widget", "A sturdy general-purpose widget.", 9.99m),
            new("gadget", "Gadget", "Does the thing gadgets do.", 24.50m),
            new("gizmo", "Gizmo", "Small, shiny, surprisingly useful.", 14.00m),
        ];
        foreach (var p in seed) await container.UpsertItemAsync(p, new PartitionKey(p.Id));
    }
    catch (Exception e) { app.Logger.LogWarning(e, "Catalog seeding skipped"); }
});

app.Run();
