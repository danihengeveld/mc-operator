using k8s.Models;
using McOperator.Entities;
using McOperator.Extensions;

namespace McOperator.Builders;

/// <summary>
/// Builds Kubernetes resources for the Velocity proxy in a MinecraftServerCluster.
/// Manages the proxy Deployment, Service, and ConfigMap (velocity.toml).
/// </summary>
/// <remarks>
/// The Velocity proxy is deployed as a Deployment (not StatefulSet) because:
/// - The proxy is stateless; all player routing state is ephemeral
/// - A single replica is sufficient since proxy traffic is lightweight
/// - No persistent storage is needed
///
/// The itzg/mc-proxy image is used with TYPE=VELOCITY for consistency
/// with the itzg/minecraft-server image used for backend servers.
/// </remarks>
public static class VelocityProxyBuilder
{
    private const string ContainerName = "velocity-proxy";
    private const string ConfigVolumeName = "proxy-config";
    private const string SecretVolumeName = "forwarding-secret";
    private const string DefaultImage = OperatorConstants.DefaultProxyImage;

    /// <summary>
    /// Builds the Velocity proxy Deployment.
    /// </summary>
    public static V1Deployment BuildDeployment(MinecraftServerCluster cluster)
    {
        var name = cluster.ProxyName();
        var ns = cluster.Namespace();
        var labels = BuildProxyLabels(cluster);

        var container = BuildContainer(cluster);

        return new V1Deployment
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = ns,
                Labels = labels,
                OwnerReferences = new List<V1OwnerReference> { cluster.MakeOwnerReference(), },
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Selector = new V1LabelSelector { MatchLabels = labels, },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels, },
                    Spec = new V1PodSpec
                    {
                        Containers = new List<V1Container> { container },
                        Volumes = BuildVolumes(cluster),
                        TerminationGracePeriodSeconds = 30,
                    },
                },
            },
        };
    }

    /// <summary>
    /// Builds the Velocity proxy Service.
    /// </summary>
    public static V1Service BuildService(MinecraftServerCluster cluster)
    {
        var name = cluster.ProxyServiceName();
        var ns = cluster.Namespace();
        var spec = cluster.Spec.Proxy;
        var labels = BuildProxyLabels(cluster);
        var selectorLabels = BuildProxySelectorLabels(cluster);

        var ports = new List<V1ServicePort>
        {
            new()
            {
                Name = "minecraft",
                Protocol = "TCP",
                Port = spec.ProxyPort,
                TargetPort = spec.ProxyPort,
                NodePort = spec.Service.Type == ServiceType.NodePort ? spec.Service.NodePort : null,
            },
        };

        var annotations = new Dictionary<string, string>(spec.Service.Annotations);

        return new V1Service
        {
            ApiVersion = "v1",
            Kind = "Service",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = ns,
                Labels = labels,
                Annotations = annotations.Count > 0 ? annotations : null,
                OwnerReferences = new List<V1OwnerReference> { cluster.MakeOwnerReference(), },
            },
            Spec = new V1ServiceSpec { Type = spec.Service.Type.ToString(), Selector = selectorLabels, Ports = ports, },
        };
    }

    /// <summary>
    /// Builds the ConfigMap containing velocity.toml and forwarding.secret.
    /// </summary>
    public static V1ConfigMap BuildConfigMap(
        MinecraftServerCluster cluster,
        IList<ServerAddress> serverAddresses,
        string forwardingSecret)
    {
        var name = cluster.ProxyConfigMapName();
        var ns = cluster.Namespace();
        var labels = BuildProxyLabels(cluster);

        var velocityToml = BuildVelocityToml(cluster, serverAddresses);

        return new V1ConfigMap
        {
            ApiVersion = "v1",
            Kind = "ConfigMap",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = ns,
                Labels = labels,
                OwnerReferences = new List<V1OwnerReference> { cluster.MakeOwnerReference(), },
            },
            Data = new Dictionary<string, string>
            {
                ["velocity.toml"] = velocityToml, ["forwarding.secret"] = forwardingSecret,
            },
        };
    }

    /// <summary>
    /// Generates the velocity.toml configuration file content.
    /// Note: config-version "2.7" corresponds to Velocity 3.x format.
    /// If a future Velocity release requires a new config format, this will need updating.
    /// </summary>
    public static string BuildVelocityToml(
        MinecraftServerCluster cluster,
        IList<ServerAddress> serverAddresses)
    {
        var spec = cluster.Spec.Proxy;
        var motd = spec.Motd ?? $"A Minecraft Cluster ({cluster.Name()})";
        var forwardingMode = spec.PlayerForwardingMode.ToString().ToLowerInvariant();

        var lines = new List<string>
        {
            "# Managed by mc-operator - do not edit manually",
            "config-version = \"2.7\"",
            $"bind = \"0.0.0.0:{spec.ProxyPort}\"",
            $"motd = \"{EscapeTomlString(motd)}\"",
            $"show-max-players = {spec.MaxPlayers}",
            $"online-mode = {spec.OnlineMode.ToString().ToLowerInvariant()}",
            $"player-info-forwarding-mode = \"{forwardingMode}\"",
            "",
            "[servers]",
        };

        // Register each backend server
        foreach (var server in serverAddresses)
        {
            lines.Add($"{server.Name} = \"{server.Address}:{server.Port}\"");
        }

        // Try order: determines which servers players attempt to join first
        var tryOrder = BuildTryOrder(cluster, serverAddresses);
        lines.Add($"try = [{string.Join(", ", tryOrder.Select(s => $"\"{s}\""))}]");

        lines.Add("");
        lines.Add("[forced-hosts]");
        lines.Add("");
        lines.Add("[advanced]");
        lines.Add("compression-threshold = 256");
        lines.Add("compression-level = -1");
        lines.Add("login-ratelimit = 3000");
        lines.Add("connection-timeout = 5000");
        lines.Add("read-timeout = 30000");
        lines.Add("haproxy-protocol = false");
        lines.Add("tcp-fast-open = false");
        lines.Add("bungee-plugin-message-channel = true");
        lines.Add("show-ping-requests = false");
        lines.Add("failover-on-unexpected-server-disconnect = true");
        lines.Add("announce-proxy-commands = true");
        lines.Add("log-command-executions = false");
        lines.Add("log-player-connections = true");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Resolves the container image for the Velocity proxy.
    /// </summary>
    public static string ResolveImage(VelocityProxySpec spec)
    {
        if (!string.IsNullOrEmpty(spec.Image))
        {
            return spec.Image;
        }

        return DefaultImage;
    }

    /// <summary>
    /// Generates a random forwarding secret for Velocity modern forwarding.
    /// </summary>
    public static string GenerateForwardingSecret()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static V1Container BuildContainer(
        MinecraftServerCluster cluster)
    {
        var spec = cluster.Spec.Proxy;
        var image = ResolveImage(spec);

        var env = new List<V1EnvVar> { Env("TYPE", "VELOCITY"), Env("VELOCITY_VERSION", spec.Version), };

        var ports = new List<V1ContainerPort>
        {
            new() { Name = "minecraft", ContainerPort = spec.ProxyPort, Protocol = "TCP" },
        };

        var volumeMounts = new List<V1VolumeMount>
        {
            new()
            {
                Name = ConfigVolumeName,
                MountPath = "/server/velocity.toml",
                SubPath = "velocity.toml",
                ReadOnlyProperty = true,
            },
            new()
            {
                Name = SecretVolumeName,
                MountPath = "/server/forwarding.secret",
                SubPath = "forwarding.secret",
                ReadOnlyProperty = true,
            },
        };

        var resources = BuildResourceRequirements(spec.Resources);

        return new V1Container
        {
            Name = ContainerName,
            Image = image,
            ImagePullPolicy = spec.ImagePullPolicy.ToString(),
            Env = env,
            Ports = ports,
            VolumeMounts = volumeMounts,
            Resources = resources,
            ReadinessProbe = new V1Probe
            {
                TcpSocket = new V1TCPSocketAction { Port = spec.ProxyPort },
                InitialDelaySeconds = 15,
                PeriodSeconds = 10,
                FailureThreshold = 6,
            },
            LivenessProbe = new V1Probe
            {
                TcpSocket = new V1TCPSocketAction { Port = spec.ProxyPort },
                InitialDelaySeconds = 30,
                PeriodSeconds = 30,
                FailureThreshold = 3,
            },
        };
    }

    private static List<V1Volume> BuildVolumes(MinecraftServerCluster cluster)
    {
        var configMapName = cluster.ProxyConfigMapName();

        return
        [
            new V1Volume
            {
                Name = ConfigVolumeName,
                ConfigMap = new V1ConfigMapVolumeSource
                {
                    Name = configMapName,
                    Items = new List<V1KeyToPath> { new() { Key = "velocity.toml", Path = "velocity.toml" }, },
                },
            },
            new V1Volume
            {
                Name = SecretVolumeName,
                ConfigMap = new V1ConfigMapVolumeSource
                {
                    Name = configMapName,
                    Items = new List<V1KeyToPath>
                    {
                        new() { Key = "forwarding.secret", Path = "forwarding.secret" },
                    },
                },
            }
        ];
    }

    private static V1ResourceRequirements BuildResourceRequirements(ResourcesSpec resources)
    {
        var memLimit = resources.MemoryLimit ?? resources.MemoryRequest;

        var req = new Dictionary<string, ResourceQuantity>();
        var limits = new Dictionary<string, ResourceQuantity>();

        if (resources.CpuRequest is not null)
        {
            req["cpu"] = new ResourceQuantity(resources.CpuRequest);
        }

        if (resources.MemoryRequest is not null)
        {
            req["memory"] = new ResourceQuantity(resources.MemoryRequest);
        }

        if (resources.CpuLimit is not null)
        {
            limits["cpu"] = new ResourceQuantity(resources.CpuLimit);
        }

        if (memLimit is not null)
        {
            limits["memory"] = new ResourceQuantity(memLimit);
        }

        return new V1ResourceRequirements
        {
            Requests = req.Count > 0 ? req : null, Limits = limits.Count > 0 ? limits : null,
        };
    }

    private static IList<string> BuildTryOrder(
        MinecraftServerCluster cluster,
        IList<ServerAddress> serverAddresses)
    {
        var tryOrder = cluster.Spec.Proxy.TryOrder;
        if (tryOrder.Count > 0)
        {
            // Use specified order, mapping indices to server names
            return tryOrder
                .Where(i => i >= 0 && i < serverAddresses.Count)
                .Select(i => serverAddresses[i].Name)
                .ToList();
        }

        // Default: all servers in ascending index order
        return serverAddresses.Select(s => s.Name).ToList();
    }

    private static V1EnvVar Env(string name, string value) =>
        new() { Name = name, Value = value };

    private static string EscapeTomlString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static IDictionary<string, string> BuildProxyLabels(MinecraftServerCluster cluster) =>
        new Dictionary<string, string>
        {
            [OperatorConstants.AppNameLabel] = OperatorConstants.ProxyAppName,
            [OperatorConstants.AppInstanceLabel] = cluster.ProxyName(),
            [OperatorConstants.AppManagedByLabel] = OperatorConstants.OperatorName,
            [OperatorConstants.AppComponentLabel] = OperatorConstants.ComponentProxy,
            [OperatorConstants.ClusterNameLabel] = cluster.Name(),
        };

    private static IDictionary<string, string> BuildProxySelectorLabels(MinecraftServerCluster cluster) =>
        new Dictionary<string, string>
        {
            [OperatorConstants.AppInstanceLabel] = cluster.ProxyName(),
            [OperatorConstants.ClusterNameLabel] = cluster.Name(),
        };
}

/// <summary>
/// Represents a backend server address for Velocity proxy configuration.
/// </summary>
public class ServerAddress
{
    /// <summary>
    /// Logical name of the server (used in velocity.toml).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Hostname or IP address of the server.
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// Port the server listens on.
    /// </summary>
    public required int Port { get; init; }
}
