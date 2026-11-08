using Platform.Common;
using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;
using Sheridan.Aspire.Pulumi.Azure.CosmosDB;
using Sheridan.Aspire.Pulumi.Azure.ServiceBus;
using Sheridan.Aspire.Pulumi.Azure.Storage;

var builder = DistributedApplication.CreateBuilder(args);

// ---- Platform inputs -------------------------------------------------------------------
// The shared and environment layers are never created here. Their names come from the same
// naming library those layers use, driven by these three values (appsettings or Platform__* env vars).
var workload = builder.Configuration["Platform:Workload"] ?? "plat";
var suffix = builder.Configuration["Platform:NameSuffix"] ?? throw new InvalidOperationException("Platform:NameSuffix is required");
var env = builder.Configuration["Platform:Environment"] ?? "test";
var location = builder.Configuration["Platform:Location"] ?? "westus2";
var names = new PlatformNaming(workload, suffix);

// ---- Pulumi: project "platform", stack "app-<env>", ESC azure/<env> --------------------
var stack = builder.AddPulumiStack("app", $"app-{env}")
    .WithOrganization(builder.Configuration["Pulumi:Organization"] ?? "two4suited")
    .WithProjectName(builder.Configuration["Pulumi:Project"] ?? "platform")
    .WithEscEnvironment($"azure/{env}");

// One resource group holds everything the app creates (the identity, container apps, storage, Cosmos).
var appRg = builder.AddPulumiAzureResourceGroup("rg-app")
    .WithPulumiStack(stack)
    .WithLocation(location)
    .WithAzureName(AzureNameKind.ResourceGroup, $"rg-{workload}-app-{env}");

// ---- Existing platform resources (looked up, never created) ----------------------------
var acr = builder.AddPulumiAzureContainerRegistry("acr")
    .WithPulumiStack(stack)
    .AsExisting(names.SharedContainerRegistry, names.SharedResourceGroup);

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca")
    .WithPulumiStack(stack)
    .AsExisting(names.ContainerAppsEnvironment(env), names.EnvResourceGroup(env))
    .WithAzureContainerRegistry(acr)
    .WithResourceGroup(appRg)
    .WithLocation(location)
    .WithAzureName(AzureNameKind.UserAssignedIdentity, $"id-{workload}-app-{env}", "mi");

// Local runs (aspire start / run) use emulators; publish and deploy use the real, existing resources.
var isRun = builder.ExecutionContext.IsRunMode;

IResourceBuilder<IResourceWithConnectionString> messaging;
if (isRun)
{
    // The Pulumi Service Bus integration has no emulator, so locally use the official one.
    var local = builder.AddAzureServiceBus("messaging").RunAsEmulator();
    var localEvents = local.AddServiceBusTopic("order-events");
    localEvents.AddServiceBusSubscription("fulfillment");
    localEvents.AddServiceBusSubscription("audit");
    local.AddServiceBusQueue("invoice-requests");
    messaging = local;
}
else
{
    var existing = builder.AddPulumiServiceBus("messaging")
        .WithPulumiStack(stack)
        .AsExisting(names.ServiceBus(env), names.EnvResourceGroup(env));
    var orderEvents = existing.AddServiceBusTopic("order-events", "order-events");
    orderEvents.AddServiceBusSubscription("fulfillment", "fulfillment");
    orderEvents.AddServiceBusSubscription("audit", "audit");
    existing.AddServiceBusQueue("invoice-requests", "invoice-requests");
    messaging = existing;
}

// ---- App-owned resources ----------------------------------------------------------------
var storage = builder.AddPulumiStorage("storage")
    .WithPulumiStack(stack)
    .WithResourceGroup(appRg)
    .WithLocation(location)
    .WithSku(env == "prod" ? "Standard_GRS" : "Standard_LRS")
    .WithAzureName(AzureNameKind.StorageAccount, $"st{workload}app{env}{suffix}");
if (isRun) storage.RunAsEmulator();
var tables = storage.AddTables("tables");
var queues = storage.AddQueues("queues");
storage.AddQueue("invoice-queue", "invoice-requests");

var cosmos = builder.AddPulumiCosmosDB("cosmos")
    .WithPulumiStack(stack)
    .WithResourceGroup(appRg)
    .WithLocation(location)
    .WithAzureName(AzureNameKind.CosmosDbAccount, $"cosmos-{workload}-app-{env}-{suffix}");
if (isRun) cosmos.RunAsEmulator();
var ordersDb = cosmos.AddCosmosDatabase("ordersdb", "ordersdb");
ordersDb.AddContainer("products", "/id", "products");
ordersDb.AddContainer("orders", "/id", "orders");

// ---- Workloads --------------------------------------------------------------------------
var catalog = builder.AddProject<Projects.Orders_CatalogApi>("catalog-api")
    .WithExternalHttpEndpoints()          // reached by API Management, which fronts it
    .WithComputeEnvironment(acaEnv)
    .WithReference(cosmos);

var ordersApi = builder.AddProject<Projects.Orders_OrdersApi>("orders-api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(cosmos)
    .WithReference(messaging)
    .WithReference(catalog);

builder.AddProject<Projects.Orders_FulfillmentJob>("fulfillment-job")
    .WithComputeEnvironment(acaEnv)
    .WithReference(cosmos)
    .WithReference(messaging)
    .WithReference(tables)
    .WithReference(queues)
    .PublishAsScheduledPulumiContainerAppJob("0 2 * * *");   // nightly

builder.AddProject<Projects.Orders_Web>("web")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(catalog)
    .WithReference(ordersApi);

builder.Build().Run();
