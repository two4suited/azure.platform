namespace Platform.Common;

/// <summary>
/// Single source of truth for resource names. Pulumi (shared, environment) and Aspire (app)
/// all derive names from the same inputs, so nothing needs a stack reference just to find a name.
/// </summary>
/// <param name="Workload">Short workload name, for example "plat".</param>
/// <param name="Suffix">Makes globally unique names unique (Key Vault, ACR, App Configuration, Service Bus).</param>
public sealed record PlatformNaming(string Workload, string Suffix)
{
    // ---- Shared ----
    public string SharedResourceGroup => $"rg-{Workload}-shared";
    public string SharedKeyVault => $"kv-{Workload}-shared-{Suffix}";
    public string SharedContainerRegistry => $"acr{Workload}shared{Suffix}";
    public string SharedAppConfiguration => $"appcs-{Workload}-shared-{Suffix}";

    public string SharedKeyVaultUri => $"https://{SharedKeyVault}.vault.azure.net/";
    public string SharedContainerRegistryLoginServer => $"{SharedContainerRegistry}.azurecr.io";
    public string SharedAppConfigurationEndpoint => $"https://{SharedAppConfiguration}.azconfig.io";

    // ARM resource ids of the shared resources. Needed for private endpoints, no stack reference required.
    public string SharedKeyVaultId(string subscriptionId) =>
        SharedResourceId(subscriptionId, "Microsoft.KeyVault/vaults", SharedKeyVault);
    public string SharedContainerRegistryId(string subscriptionId) =>
        SharedResourceId(subscriptionId, "Microsoft.ContainerRegistry/registries", SharedContainerRegistry);
    public string SharedAppConfigurationId(string subscriptionId) =>
        SharedResourceId(subscriptionId, "Microsoft.AppConfiguration/configurationStores", SharedAppConfiguration);

    private string SharedResourceId(string subscriptionId, string type, string name) =>
        $"/subscriptions/{subscriptionId}/resourceGroups/{SharedResourceGroup}/providers/{type}/{name}";

    // ---- Environment ----
    public string EnvResourceGroup(string env) => $"rg-{Workload}-{env}";
    public string VirtualNetwork(string env) => $"vnet-{Workload}-{env}";
    public string ServiceBus(string env) => $"sb-{Workload}-{env}-{Suffix}";
    public string ContainerAppsEnvironment(string env) => $"cae-{Workload}-{env}";
    public string ApiManagement(string env) => $"apim-{Workload}-{env}-{Suffix}";
    public string FrontDoor(string env) => $"afd-{Workload}-{env}";
    public string AiSearch(string env) => $"srch-{Workload}-{env}-{Suffix}";
    public string AiFoundry(string env) => $"aif-{Workload}-{env}-{Suffix}";
}

/// <summary>Subnet names, shared by every environment.</summary>
public static class SubnetNames
{
    public const string Apim = "snet-apim";
    public const string PrivateEndpoints = "snet-pe";
    public const string ContainerApps = "snet-aca";
}

/// <summary>Address plan from the README: one /16 per environment, so VNets can be peered later.</summary>
public sealed record EnvironmentNetwork(string VnetCidr, string ApimCidr, string PrivateEndpointsCidr, string ContainerAppsCidr)
{
    public static EnvironmentNetwork For(string env) => env switch
    {
        "test" => FromOctet(10),
        "uat" => FromOctet(20),
        "prod" => FromOctet(30),
        _ => throw new ArgumentException($"No address plan for environment '{env}'. Add it to EnvironmentNetwork.", nameof(env)),
    };

    private static EnvironmentNetwork FromOctet(int second) => new(
        VnetCidr: $"10.{second}.0.0/16",
        ApimCidr: $"10.{second}.0.0/27",
        PrivateEndpointsCidr: $"10.{second}.0.64/26",
        ContainerAppsCidr: $"10.{second}.2.0/23");
}

/// <summary>Private DNS zone names per service type, created per environment.</summary>
public static class PrivateDnsZones
{
    public const string KeyVault = "privatelink.vaultcore.azure.net";
    public const string ContainerRegistry = "privatelink.azurecr.io";
    public const string AppConfiguration = "privatelink.azconfig.io";
    public const string ServiceBus = "privatelink.servicebus.windows.net";
    public const string Search = "privatelink.search.windows.net";
    public const string CognitiveServices = "privatelink.cognitiveservices.azure.com";
    public const string OpenAi = "privatelink.openai.azure.com";

    public static readonly string[] All =
    [
        KeyVault, ContainerRegistry, AppConfiguration, ServiceBus, Search, CognitiveServices, OpenAi,
    ];
}
