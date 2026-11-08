using Orders.FulfillmentJob;

// Runs as a scheduled Container Apps job: drain what is waiting, then exit.
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddAzureCosmosClient("cosmos");
builder.AddAzureServiceBusClient("messaging");
builder.AddAzureTableServiceClient("tables");
builder.AddAzureQueueServiceClient("queues");
builder.Services.AddSingleton<FulfillmentRunner>();
builder.Services.AddHostedService<Worker>();

builder.Build().Run();
