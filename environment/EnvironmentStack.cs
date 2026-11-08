using Pulumi;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;
using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.Network;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.ServiceBus;
using Platform.Common;
using Apim = Pulumi.AzureNative.ApiManagement;
using Afd = Pulumi.AzureNative.Cdn;
using Cog = Pulumi.AzureNative.CognitiveServices;
using Srch = Pulumi.AzureNative.Search;
using NetIn = Pulumi.AzureNative.Network.Inputs;
using PrivateDnsLink = Pulumi.AzureNative.PrivateDns.VirtualNetworkLink;
using PrivateDnsLinkArgs = Pulumi.AzureNative.PrivateDns.VirtualNetworkLinkArgs;
using PrivateDnsZone = Pulumi.AzureNative.PrivateDns.PrivateZone;
using PrivateDnsZoneArgs = Pulumi.AzureNative.PrivateDns.PrivateZoneArgs;
using SbSkuArgs = Pulumi.AzureNative.ServiceBus.Inputs.SBSkuArgs;

namespace Platform.Environment;

/// <summary>
/// One environment (test, uat, prod): network, private DNS, Service Bus, Container Apps environment,
/// and private endpoints to the shared services. The stack name is the environment name.
/// </summary>
public class EnvironmentStack : Stack
{
    [Output("environmentName")] public Output<string> EnvironmentName { get; set; }
    [Output("resourceGroupName")] public Output<string> ResourceGroupName { get; set; }
    [Output("virtualNetworkName")] public Output<string> VirtualNetworkName { get; set; }
    [Output("serviceBusNamespace")] public Output<string> ServiceBusNamespace { get; set; }
    [Output("serviceBusEndpoint")] public Output<string> ServiceBusEndpoint { get; set; }
    [Output("containerAppsEnvironmentName")] public Output<string> ContainerAppsEnvironmentName { get; set; }
    [Output("containerAppsEnvironmentId")] public Output<string> ContainerAppsEnvironmentId { get; set; }
    [Output("apiManagementName")] public Output<string> ApiManagementName { get; set; }
    [Output("apiManagementGatewayUrl")] public Output<string> ApiManagementGatewayUrl { get; set; }
    [Output("frontDoorApiHostname")] public Output<string> FrontDoorApiHostname { get; set; }
    [Output("frontDoorWebHostname")] public Output<string> FrontDoorWebHostname { get; set; }
    [Output("searchEndpoint")] public Output<string> SearchEndpoint { get; set; }
    [Output("foundryEndpoint")] public Output<string> FoundryEndpoint { get; set; }
    [Output("containerAppsDefaultDomain")] public Output<string> ContainerAppsDefaultDomain { get; set; }

    public EnvironmentStack()
    {
        var env = Pulumi.Deployment.Instance.StackName;
        var config = new Config();
        var workload = config.Get("workload") ?? "plat";
        var names = new PlatformNaming(workload, config.Require("nameSuffix"));
        var net = EnvironmentNetwork.For(env);
        var location = config.Get("location") ?? "westus2";
        // Private endpoints need Premium Service Bus. Standard is used where cost matters (test).
        var serviceBusSku = config.Get("serviceBusSku") ?? "Standard";
        var apimPublisherEmail = config.Get("apimPublisherEmail") ?? "platform-admin@example.com";
        var apimPublisherName = config.Get("apimPublisherName") ?? "Platform";
        var searchSku = config.Get("searchSku") ?? "basic";
        var frontDoorSku = config.Get("frontDoorSku") ?? "Standard_AzureFrontDoor";
        var sharedPrivateEndpoints = config.GetBoolean("sharedPrivateEndpoints") ?? true;

        var tags = new InputMap<string>
        {
            { "layer", "environment" },
            { "environment", env },
            { "workload", workload },
            { "managedBy", "pulumi" },
        };

        var subscriptionId = GetClientConfig.Invoke().Apply(c => c.SubscriptionId);

        var rg = new ResourceGroup("rg-env", new ResourceGroupArgs
        {
            ResourceGroupName = names.EnvResourceGroup(env),
            Location = location,
            Tags = tags,
        });

        // ---- Network ----
        var nsgApim = Nsg("nsg-apim", $"nsg-{workload}-{env}-apim");
        var nsgPe = Nsg("nsg-pe", $"nsg-{workload}-{env}-pe");
        var nsgAca = Nsg("nsg-aca", $"nsg-{workload}-{env}-aca");

        var vnet = new VirtualNetwork("vnet", new VirtualNetworkArgs
        {
            VirtualNetworkName = names.VirtualNetwork(env),
            ResourceGroupName = rg.Name,
            Location = rg.Location,
            AddressSpace = new NetIn.AddressSpaceArgs { AddressPrefixes = { net.VnetCidr } },
            Subnets =
            {
                new NetIn.SubnetArgs
                {
                    Name = SubnetNames.Apim,
                    AddressPrefix = net.ApimCidr,
                    NetworkSecurityGroup = new NetIn.NetworkSecurityGroupArgs { Id = nsgApim.Id },
                    // APIM Standard v2 uses outbound VNet integration through a delegated subnet.
                    Delegations = { new NetIn.DelegationArgs { Name = "apim", ServiceName = "Microsoft.Web/serverFarms" } },
                },
                new NetIn.SubnetArgs
                {
                    Name = SubnetNames.PrivateEndpoints,
                    AddressPrefix = net.PrivateEndpointsCidr,
                    NetworkSecurityGroup = new NetIn.NetworkSecurityGroupArgs { Id = nsgPe.Id },
                },
                new NetIn.SubnetArgs
                {
                    Name = SubnetNames.ContainerApps,
                    AddressPrefix = net.ContainerAppsCidr,
                    NetworkSecurityGroup = new NetIn.NetworkSecurityGroupArgs { Id = nsgAca.Id },
                    Delegations = { new NetIn.DelegationArgs { Name = "aca", ServiceName = "Microsoft.App/environments" } },
                },
            },
            Tags = tags,
        });

        Output<string> SubnetId(string name) =>
            vnet.Subnets.Apply(subnets => subnets!.First(s => s.Name == name).Id!);

        var peSubnetId = SubnetId(SubnetNames.PrivateEndpoints);
        var acaSubnetId = SubnetId(SubnetNames.ContainerApps);

        // ---- Private DNS, one set of zones per environment, linked to this VNet ----
        var zones = new Dictionary<string, PrivateDnsZone>();
        foreach (var zoneName in PrivateDnsZones.All)
        {
            var key = zoneName.Replace(".", "-");
            var zone = new PrivateDnsZone($"dns-{key}", new PrivateDnsZoneArgs
            {
                PrivateZoneName = zoneName,
                ResourceGroupName = rg.Name,
                Location = "global",
                Tags = tags,
            });
            _ = new PrivateDnsLink($"dnslink-{key}", new PrivateDnsLinkArgs
            {
                VirtualNetworkLinkName = $"link-{names.VirtualNetwork(env)}",
                PrivateZoneName = zone.Name,
                ResourceGroupName = rg.Name,
                Location = "global",
                RegistrationEnabled = false,
                VirtualNetwork = new Pulumi.AzureNative.PrivateDns.Inputs.SubResourceArgs { Id = vnet.Id },
                Tags = tags,
            });
            zones[zoneName] = zone;
        }

        // ---- Service Bus ----
        var isPremiumBus = serviceBusSku.Equals("Premium", StringComparison.OrdinalIgnoreCase);
        var serviceBus = new Namespace("sb", new NamespaceArgs
        {
            NamespaceName = names.ServiceBus(env),
            ResourceGroupName = rg.Name,
            Location = rg.Location,
            Sku = new SbSkuArgs
            {
                Name = isPremiumBus ? Pulumi.AzureNative.ServiceBus.SkuName.Premium : Pulumi.AzureNative.ServiceBus.SkuName.Standard,
                Tier = isPremiumBus ? Pulumi.AzureNative.ServiceBus.SkuTier.Premium : Pulumi.AzureNative.ServiceBus.SkuTier.Standard,
                Capacity = isPremiumBus ? 1 : null,
            },
            DisableLocalAuth = true,
            MinimumTlsVersion = "1.2",
            PublicNetworkAccess = isPremiumBus ? "Disabled" : "Enabled",
            Tags = tags,
        });

        // ---- Container Apps environment ----
        var containerAppsEnvironment = new ManagedEnvironment("cae", new ManagedEnvironmentArgs
        {
            EnvironmentName = names.ContainerAppsEnvironment(env),
            ResourceGroupName = rg.Name,
            Location = rg.Location,
            VnetConfiguration = new VnetConfigurationArgs
            {
                InfrastructureSubnetId = acaSubnetId,
                // Public ingress for now. Front Door is added later and the environment can go internal.
                Internal = false,
            },
            WorkloadProfiles =
            {
                new WorkloadProfileArgs { Name = "Consumption", WorkloadProfileType = "Consumption" },
            },
            ZoneRedundant = false,
            Tags = tags,
        });

        // ---- API Management (Standard v2: outbound VNet integration through snet-apim) ----
        var apimService = new Apim.ApiManagementService("apim", new Apim.ApiManagementServiceArgs
        {
            ServiceName = names.ApiManagement(env),
            ResourceGroupName = rg.Name,
            Location = rg.Location,
            PublisherEmail = apimPublisherEmail,
            PublisherName = apimPublisherName,
            Sku = new Apim.Inputs.ApiManagementServiceSkuPropertiesArgs { Name = "StandardV2", Capacity = 1 },
            VirtualNetworkType = "External",
            VirtualNetworkConfiguration = new Apim.Inputs.VirtualNetworkConfigurationArgs
            {
                SubnetResourceId = SubnetId(SubnetNames.Apim),
            },
            Identity = new Apim.Inputs.ApiManagementServiceIdentityArgs { Type = Apim.ApimIdentityType.SystemAssigned },
            Tags = tags,
        });

        // ---- AI Search ----
        var search = new Srch.Service("search", new Srch.ServiceArgs
        {
            SearchServiceName = names.AiSearch(env),
            ResourceGroupName = rg.Name,
            Location = rg.Location,
            Sku = new Srch.Inputs.SkuArgs { Name = Srch.SkuName.Basic },
            ReplicaCount = 1,
            PartitionCount = 1,
            HostingMode = Srch.HostingMode.Default,
            DisableLocalAuth = true,
            PublicNetworkAccess = Srch.PublicNetworkAccess.Enabled,
            Identity = new Srch.Inputs.IdentityArgs { Type = Srch.IdentityType.SystemAssigned },
            Tags = tags,
        });

        // ---- AI Foundry (AIServices account) ----
        var foundry = new Cog.Account("foundry", new Cog.AccountArgs
        {
            AccountName = names.AiFoundry(env),
            ResourceGroupName = rg.Name,
            Location = rg.Location,
            Kind = "AIServices",
            Sku = new Cog.Inputs.SkuArgs { Name = "S0" },
            Identity = new Cog.Inputs.IdentityArgs { Type = Cog.ResourceIdentityType.SystemAssigned },
            Properties = new Cog.Inputs.AccountPropertiesArgs
            {
                CustomSubDomainName = names.AiFoundry(env),
                DisableLocalAuth = true,
                PublicNetworkAccess = "Enabled",
                AllowProjectManagement = true,
            },
            Tags = tags,
        });

        // ---- Front Door: one profile, an api endpoint to APIM and a web endpoint for web apps ----
        var afdProfile = new Afd.Profile("afd", new Afd.ProfileArgs
        {
            ProfileName = names.FrontDoor(env),
            ResourceGroupName = rg.Name,
            Location = "global",
            Sku = new Afd.Inputs.SkuArgs { Name = frontDoorSku },
            Tags = tags,
        });

        var apiEndpoint = new Afd.AFDEndpoint("afd-api", new Afd.AFDEndpointArgs
        {
            EndpointName = names.FrontDoorApiEndpoint(env),
            ProfileName = afdProfile.Name,
            ResourceGroupName = rg.Name,
            Location = "global",
            EnabledState = Afd.EnabledState.Enabled,
            Tags = tags,
        });

        var webEndpoint = new Afd.AFDEndpoint("afd-web", new Afd.AFDEndpointArgs
        {
            EndpointName = names.FrontDoorWebEndpoint(env),
            ProfileName = afdProfile.Name,
            ResourceGroupName = rg.Name,
            Location = "global",
            EnabledState = Afd.EnabledState.Enabled,
            Tags = tags,
        });

        var apimOriginGroup = new Afd.AFDOriginGroup("afd-apim-og", new Afd.AFDOriginGroupArgs
        {
            OriginGroupName = "apim",
            ProfileName = afdProfile.Name,
            ResourceGroupName = rg.Name,
            LoadBalancingSettings = new Afd.Inputs.LoadBalancingSettingsParametersArgs
            {
                SampleSize = 4,
                SuccessfulSamplesRequired = 3,
            },
            HealthProbeSettings = new Afd.Inputs.HealthProbeParametersArgs
            {
                ProbePath = "/status-0123456789abcdef",
                ProbeRequestType = Afd.HealthProbeRequestType.GET,
                ProbeProtocol = Afd.ProbeProtocol.Https,
                ProbeIntervalInSeconds = 100,
            },
        });

        var apimOrigin = new Afd.AFDOrigin("afd-apim-origin", new Afd.AFDOriginArgs
        {
            OriginName = "apim",
            OriginGroupName = apimOriginGroup.Name,
            ProfileName = afdProfile.Name,
            ResourceGroupName = rg.Name,
            HostName = names.ApiManagementGatewayHost(env),
            OriginHostHeader = names.ApiManagementGatewayHost(env),
            HttpPort = 80,
            HttpsPort = 443,
            Priority = 1,
            Weight = 1000,
            EnabledState = Afd.EnabledState.Enabled,
            EnforceCertificateNameCheck = true,
        }, new CustomResourceOptions { DependsOn = { apimService } });

        _ = new Afd.Route("afd-api-route", new Afd.RouteArgs
        {
            RouteName = "api",
            EndpointName = apiEndpoint.Name,
            ProfileName = afdProfile.Name,
            ResourceGroupName = rg.Name,
            OriginGroup = new Afd.Inputs.ResourceReferenceArgs { Id = apimOriginGroup.Id },
            PatternsToMatch = { "/*" },
            SupportedProtocols = { Afd.AFDEndpointProtocols.Https },
            ForwardingProtocol = Afd.ForwardingProtocol.HttpsOnly,
            HttpsRedirect = Afd.HttpsRedirect.Enabled,
            LinkToDefaultDomain = Afd.LinkToDefaultDomain.Enabled,
            EnabledState = Afd.EnabledState.Enabled,
        }, new CustomResourceOptions { DependsOn = { apimOrigin } });

        // ---- Private endpoints ----
        if (isPremiumBus)
        {
            PrivateEndpointFor("sb", serviceBus.Id, "namespace", PrivateDnsZones.ServiceBus);
        }

        PrivateEndpointFor("search", search.Id, "searchService", PrivateDnsZones.Search);
        PrivateEndpointFor("foundry", foundry.Id, "account",
            PrivateDnsZones.CognitiveServices, PrivateDnsZones.OpenAi, PrivateDnsZones.AiServices);

        if (sharedPrivateEndpoints)
        {
            PrivateEndpointFor("kv", subscriptionId.Apply(names.SharedKeyVaultId), "vault", PrivateDnsZones.KeyVault);
            PrivateEndpointFor("acr", subscriptionId.Apply(names.SharedContainerRegistryId), "registry", PrivateDnsZones.ContainerRegistry);
            PrivateEndpointFor("appcs", subscriptionId.Apply(names.SharedAppConfigurationId), "configurationStores", PrivateDnsZones.AppConfiguration);
        }

        EnvironmentName = Output.Create(env);
        ResourceGroupName = rg.Name;
        VirtualNetworkName = vnet.Name;
        ServiceBusNamespace = serviceBus.Name;
        ServiceBusEndpoint = Output.Create($"{names.ServiceBus(env)}.servicebus.windows.net");
        ApiManagementName = apimService.Name;
        ApiManagementGatewayUrl = Output.Create($"https://{names.ApiManagementGatewayHost(env)}");
        FrontDoorApiHostname = apiEndpoint.HostName;
        FrontDoorWebHostname = webEndpoint.HostName;
        SearchEndpoint = Output.Create($"https://{names.AiSearch(env)}.search.windows.net");
        FoundryEndpoint = Output.Create($"https://{names.AiFoundry(env)}.services.ai.azure.com");
        ContainerAppsEnvironmentName = containerAppsEnvironment.Name;
        ContainerAppsEnvironmentId = containerAppsEnvironment.Id;
        ContainerAppsDefaultDomain = containerAppsEnvironment.DefaultDomain;

        // ---- Local helpers ----
        NetworkSecurityGroup Nsg(string resourceName, string nsgName) =>
            new(resourceName, new NetworkSecurityGroupArgs
            {
                NetworkSecurityGroupName = nsgName,
                ResourceGroupName = rg.Name,
                Location = rg.Location,
                Tags = tags,
            });

        void PrivateEndpointFor(string key, Input<string> targetId, string groupId, params string[] zoneNames)
        {
            var endpoint = new PrivateEndpoint($"pe-{key}", new PrivateEndpointArgs
            {
                PrivateEndpointName = $"pe-{workload}-{env}-{key}",
                ResourceGroupName = rg.Name,
                Location = rg.Location,
                Subnet = new NetIn.SubnetArgs { Id = peSubnetId },
                PrivateLinkServiceConnections =
                {
                    new NetIn.PrivateLinkServiceConnectionArgs
                    {
                        Name = $"plsc-{key}",
                        PrivateLinkServiceId = targetId,
                        GroupIds = { groupId },
                    },
                },
                Tags = tags,
            });

            _ = new PrivateDnsZoneGroup($"pe-{key}-dns", new PrivateDnsZoneGroupArgs
            {
                PrivateDnsZoneGroupName = "default",
                PrivateEndpointName = endpoint.Name,
                ResourceGroupName = rg.Name,
                PrivateDnsZoneConfigs = zoneNames
                    .Select(z => new NetIn.PrivateDnsZoneConfigArgs { Name = z.Replace(".", "-"), PrivateDnsZoneId = zones[z].Id })
                    .ToList(),
            });
        }
    }
}
