# App: Orders

A small order-processing app that shows the app layer: an Aspire AppHost that deploys workloads and app-owned data into the **existing** environment and shared resources, using [Sheridan.Aspire.Pulumi](https://brianpsheridan.com/Sheridan.Aspire.Pulumi/).

## What it does

| Project | Kind | Role |
|---|---|---|
| `Orders.Web` | Blazor Server site | Storefront: lists products, places orders. Public, routed by Front Door. |
| `Orders.CatalogApi` | Minimal API | Products in Cosmos DB (`products`). Seeds three products when empty. |
| `Orders.OrdersApi` | Minimal API | Prices lines from the catalog, stores the order in Cosmos DB (`orders`), publishes `OrderPlaced` to the `order-events` topic. |
| `Orders.FulfillmentJob` | Container Apps job, nightly (`0 2 * * *`) | Drains the `fulfillment` subscription: marks orders shipped, writes an audit row to table `auditlog`, puts an `InvoiceRequest` on the `invoice-requests` queue. |
| `Orders.Contracts` | Library | Shared records and resource names. |
| `Orders.ServiceDefaults` | Library | Aspire service defaults (telemetry, health, discovery). |
| `Orders.AppHost` | Aspire AppHost | The deployment model. |

```
Web ──► Catalog API ──► Cosmos: products
 └────► Orders API ───► Cosmos: orders
            │  publish OrderPlaced
            ▼
   Service Bus topic order-events ── sub fulfillment ──► Fulfillment job ──► Cosmos / table auditlog / queue invoice-requests
                                  └─ sub audit
```

## What is looked up and what is created

Looked up (never created), names derived from `common/Platform.Common`:

| Resource | Name |
|---|---|
| Container Registry | `PlatformNaming.SharedContainerRegistry` in `rg-{workload}-shared` |
| Container Apps environment | `cae-{workload}-{env}` in `rg-{workload}-{env}` |
| Service Bus namespace | `sb-{workload}-{env}-{suffix}` in `rg-{workload}-{env}` |

Created by the app, in `rg-{workload}-app-{env}`: managed identity `id-{workload}-app-{env}` (AcrPull, Service Bus, Storage, Cosmos data roles), the container apps and job, storage account `st{workload}app{env}{suffix}` (queue `invoice-requests`, table service), Cosmos account `cosmos-{workload}-app-{env}-{suffix}` (database `ordersdb`, containers `products`, `orders`, no keys). Inside the Service Bus namespace the app creates only its topic, subscriptions and queue.

## Inputs

`Orders.AppHost/appsettings.json`, or `Platform__*` environment variables:

| Key | Default | Meaning |
|---|---|---|
| `Platform:Workload` | `plat` | Same value as the shared/environment stacks |
| `Platform:NameSuffix` | `002` | Same value as the shared/environment stacks |
| `Platform:Environment` | `test` | Environment to deploy into |
| `Platform:Location` | `westus2` | Azure region |

The Pulumi stack is `app-{env}` in project `platform`, using ESC environment `azure/{env}`.

## Build and publish

```bash
cd app
dotnet build
cd src/Orders.AppHost
aspire publish -o ../../publish    # writes publish/app/pulumi/Pulumi.yaml
aspire deploy                      # builds images, pushes to the shared registry, runs pulumi up
```

Deploy only after `shared` and `environment` stacks are up.

## Library dependency

The AppHost uses `AsExisting` for Service Bus, Container Apps environment and Container Registry, released in Sheridan.Aspire.Pulumi 1.3.0 ([#157](https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/157)).

## Not done yet

- **APIM:** the OpenAPI documents (`/openapi/v1.json` on each API) are not imported into API Management yet. The library has no `AsExisting` for APIM.
- **Front Door web route:** the environment stack has a `web` endpoint but no origin for this app yet.
- **One identity:** the library gives all workloads in a compute environment one user-assigned identity, so per-workload RBAC is not possible yet.
- **Local run:** `aspire start` uses emulators for Cosmos DB (vNext, with Data Explorer), Azurite and the Service Bus emulator. On Apple Silicon, Docker Desktop needs the Apple Virtualization framework with "Use Rosetta for x86_64/amd64 emulation" on, or SQL Server (behind the Service Bus emulator) crashes. Open library issues: [#176](https://github.com/two4suited/Sheridan.Aspire.Pulumi/issues/176) (Azurite 3.35.0 rejects current SDK API versions, so the job's table and queue writes fail) and [#177](https://github.com/two4suited/Sheridan.Aspire.Pulumi/issues/177) (`WaitFor` the Service Bus never completes, so the job can start before the emulator is ready and needs a restart).
- **Not deployed:** this has only been published (generated Pulumi program checked), not applied to Azure.
