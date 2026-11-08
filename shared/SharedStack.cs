using Pulumi;
using Pulumi.AzureNative.AppConfiguration;
using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.ContainerRegistry;
using Pulumi.AzureNative.KeyVault;
using Pulumi.AzureNative.Resources;
using KvSku = Pulumi.AzureNative.KeyVault.SkuName;
using KvSkuFamily = Pulumi.AzureNative.KeyVault.SkuFamily;
using AcrSku = Pulumi.AzureNative.ContainerRegistry.SkuName;

namespace Platform.Shared;

/// <summary>
/// Shared services resource group: App Configuration, Key Vault, Container Registry.
/// Deployed once and consumed by every environment and app.
/// </summary>
public class SharedStack : Stack
{
    [Output("resourceGroupName")] public Output<string> ResourceGroupName { get; set; }
    [Output("appConfigName")] public Output<string> AppConfigName { get; set; }
    [Output("appConfigEndpoint")] public Output<string> AppConfigEndpoint { get; set; }
    [Output("keyVaultName")] public Output<string> KeyVaultName { get; set; }
    [Output("keyVaultUri")] public Output<string> KeyVaultUri { get; set; }
    [Output("acrName")] public Output<string> AcrName { get; set; }
    [Output("acrLoginServer")] public Output<string> AcrLoginServer { get; set; }

    public SharedStack()
    {
        var config = new Config();
        var workload = config.Get("workload") ?? "plat";
        // Key Vault, ACR and App Configuration names are globally unique, so a suffix is required.
        var suffix = config.Require("nameSuffix");
        var location = config.Get("location") ?? "westus2";

        var tags = new InputMap<string>
        {
            { "layer", "shared" },
            { "workload", workload },
            { "managedBy", "pulumi" },
        };

        var clientConfig = GetClientConfig.Invoke();

        var rg = new ResourceGroup("rg-shared", new ResourceGroupArgs
        {
            ResourceGroupName = $"rg-{workload}-shared",
            Location = location,
            Tags = tags,
        });

        // Standard is the minimum SKU for private endpoints.
        var appConfig = new ConfigurationStore("appcs", new ConfigurationStoreArgs
        {
            ConfigStoreName = $"appcs-{workload}-shared-{suffix}",
            ResourceGroupName = rg.Name,
            Location = rg.Location,
            Sku = new Pulumi.AzureNative.AppConfiguration.Inputs.SkuArgs { Name = "standard" },
            DisableLocalAuth = true,
            Tags = tags,
        });

        var vault = new Vault("kv", new VaultArgs
        {
            VaultName = $"kv-{workload}-shared-{suffix}",
            ResourceGroupName = rg.Name,
            Location = rg.Location,
            Properties = new Pulumi.AzureNative.KeyVault.Inputs.VaultPropertiesArgs
            {
                TenantId = clientConfig.Apply(c => c.TenantId),
                Sku = new Pulumi.AzureNative.KeyVault.Inputs.SkuArgs
                {
                    Family = KvSkuFamily.A,
                    Name = KvSku.Standard,
                },
                EnableRbacAuthorization = true,
                EnableSoftDelete = true,
                SoftDeleteRetentionInDays = 90,
                EnablePurgeProtection = true,
            },
            Tags = tags,
        });

        // Premium is required for private endpoints.
        var registry = new Registry("acr", new RegistryArgs
        {
            RegistryName = $"acr{workload}shared{suffix}",
            ResourceGroupName = rg.Name,
            Location = rg.Location,
            Sku = new Pulumi.AzureNative.ContainerRegistry.Inputs.SkuArgs { Name = AcrSku.Premium },
            AdminUserEnabled = false,
            Tags = tags,
        });

        ResourceGroupName = rg.Name;
        AppConfigName = appConfig.Name;
        AppConfigEndpoint = appConfig.Endpoint;
        KeyVaultName = vault.Name;
        KeyVaultUri = vault.Properties.Apply(p => p.VaultUri ?? "");
        AcrName = registry.Name;
        AcrLoginServer = registry.LoginServer;
    }
}
