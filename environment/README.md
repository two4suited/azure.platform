# environment

Pulumi (C#) program for one environment. The stack name is the environment name (`test`, `uat`, `prod`) and drives names and the address plan from `common/Platform.Common`.

Pulumi Cloud: project `platform`, stacks `test`, `uat`, `prod`.

| Resource | Name |
|---|---|
| Resource group | `rg-{workload}-{env}` |
| VNet, subnets, NSGs | `vnet-{workload}-{env}` with `snet-apim`, `snet-pe`, `snet-aca` |
| Private DNS zones | one set per environment, linked to the VNet |
| Service Bus | `sb-{workload}-{env}-{suffix}` |
| Container Apps environment | `cae-{workload}-{env}` (workload profiles, VNet integrated) |
| Private endpoints | shared Key Vault, Container Registry, App Configuration, and Service Bus when Premium |

Not built yet: API Management, Front Door, AI Foundry, AI Search.

## Config

| Key | Default | Purpose |
|---|---|---|
| `workload` | `plat` | Must match the shared stack |
| `location` | `westus2` | Azure region |
| `nameSuffix` | required | Must match the shared stack, shared resource ids are derived from it |
| `serviceBusSku` | `Standard` | `Premium` adds a private endpoint and disables public access |
| `sharedPrivateEndpoints` | `true` | Create private endpoints to the shared services. The shared stack must be deployed first. |

## Usage

```bash
cd environment
pulumi stack init test      # once
pulumi config env add azure/test
pulumi up -s test
```
