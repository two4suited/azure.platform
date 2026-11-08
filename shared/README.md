# shared

Pulumi (C#) project for the shared services resource group. Deployed once, used by every environment and app.

| Resource | Name | Notes |
|---|---|---|
| Resource group | `rg-{workload}-shared` | |
| App Configuration | `appcs-{workload}-shared-{suffix}` | Standard SKU (needed for private endpoints), local auth disabled |
| Key Vault | `kv-{workload}-shared-{suffix}` | RBAC authorization, soft delete 90 days, purge protection |
| Container Registry | `acr{workload}shared{suffix}` | Premium SKU (needed for private endpoints), admin user disabled |

## Config

| Key | Default | Purpose |
|---|---|---|
| `workload` | `plat` | Short workload name used in resource names |
| `location` | `westus2` | Azure region |
| `nameSuffix` | required | Makes the globally unique names unique (Key Vault, ACR, App Configuration) |

## Outputs

`resourceGroupName`, `appConfigName`, `appConfigEndpoint`, `keyVaultName`, `keyVaultUri`, `acrName`, `acrLoginServer`

## Usage

```bash
cd shared
az login
pulumi preview -s shared
pulumi up -s shared
```

Public network access is left on for now. Private endpoints are created per environment by the environment stack, after which public access can be turned off.
