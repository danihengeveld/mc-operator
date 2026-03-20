using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace McOperator.Entities;

/// <summary>
/// Spec for a MinecraftServerCluster resource.
/// Defines the backend server template, scaling configuration, and Velocity proxy settings.
/// </summary>
public class MinecraftServerClusterSpec
{
    /// <summary>
    /// Template for backend MinecraftServer instances.
    /// Each server created by the cluster uses this template.
    /// </summary>
    [Required]
    public MinecraftServerTemplate Template { get; set; } = new();

    /// <summary>
    /// Scaling configuration for the backend servers.
    /// </summary>
    [Required]
    public ScalingSpec Scaling { get; set; } = new();

    /// <summary>
    /// Velocity proxy configuration.
    /// </summary>
    public VelocityProxySpec Proxy { get; set; } = new();
}

/// <summary>
/// Template for backend MinecraftServer instances in a cluster.
/// Contains all the same configuration as MinecraftServerSpec except replicas,
/// which are managed by the cluster's scaling configuration.
/// </summary>
public class MinecraftServerTemplate
{
    /// <summary>
    /// Server distribution and version settings.
    /// </summary>
    [Required]
    public ServerSpec Server { get; set; } = new();

    /// <summary>
    /// Minecraft server gameplay settings (server.properties).
    /// </summary>
    public ServerPropertiesSpec Properties { get; set; } = new();

    /// <summary>
    /// JVM tuning options.
    /// </summary>
    public JvmSpec Jvm { get; set; } = new();

    /// <summary>
    /// Resource requests and limits for each server container.
    /// </summary>
    public ResourcesSpec Resources { get; set; } = new();

    /// <summary>
    /// Persistent storage configuration for each server.
    /// </summary>
    public StorageSpec Storage { get; set; } = new();

    /// <summary>
    /// Whether to accept the Minecraft EULA. Must be true to start the servers.
    /// </summary>
    [Required]
    public bool AcceptEula { get; set; } = false;

    /// <summary>
    /// Optional override for the container image.
    /// </summary>
    public string? Image { get; set; }

    /// <summary>
    /// Image pull policy. Defaults to IfNotPresent.
    /// </summary>
    public ImagePullPolicy ImagePullPolicy { get; set; } = ImagePullPolicy.IfNotPresent;
}

/// <summary>
/// Scaling configuration for a MinecraftServerCluster.
/// Supports both static (fixed replica count) and dynamic (auto-scaling) modes.
/// </summary>
public class ScalingSpec
{
    /// <summary>
    /// Scaling mode. Static uses a fixed number of replicas.
    /// Dynamic scales between min and max based on a scaling policy.
    /// </summary>
    public ScalingMode Mode { get; set; } = ScalingMode.Static;

    /// <summary>
    /// Number of server replicas for Static mode.
    /// </summary>
    [Range(1, 100)]
    public int Replicas { get; set; } = 1;

    /// <summary>
    /// Minimum number of server replicas for Dynamic mode.
    /// </summary>
    [Range(1, 100)]
    public int MinReplicas { get; set; } = 1;

    /// <summary>
    /// Maximum number of server replicas for Dynamic mode.
    /// </summary>
    [Range(1, 100)]
    public int MaxReplicas { get; set; } = 10;

    /// <summary>
    /// Scaling policy for Dynamic mode. Determines when to scale up/down.
    /// </summary>
    public ScalingPolicy? Policy { get; set; }
}

/// <summary>
/// Scaling mode for the cluster.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScalingMode
{
    /// <summary>Fixed number of replicas.</summary>
    Static,

    /// <summary>Automatically scale between min and max replicas.</summary>
    Dynamic,
}

/// <summary>
/// Defines the scaling behavior for dynamic scaling mode.
/// </summary>
public class ScalingPolicy
{
    /// <summary>
    /// The metric to use for scaling decisions.
    /// </summary>
    public ScalingMetric Metric { get; set; } = ScalingMetric.PlayerCount;

    /// <summary>
    /// Target utilization percentage. When the average utilization across all servers
    /// exceeds this value, the cluster scales up. Below this value, it may scale down.
    /// For PlayerCount: percentage of maxPlayers occupied across all servers.
    /// </summary>
    [Range(1, 100)]
    public int TargetPercentage { get; set; } = 80;
}

/// <summary>
/// Metrics used for auto-scaling decisions.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScalingMetric
{
    /// <summary>Scale based on player count relative to maxPlayers.</summary>
    PlayerCount,
}

/// <summary>
/// Velocity proxy configuration for the cluster.
/// The operator deploys and manages a Velocity proxy that routes players
/// to the backend MinecraftServer instances.
/// </summary>
public class VelocityProxySpec
{
    /// <summary>
    /// Kubernetes Service exposure settings for the proxy.
    /// This is how players connect to the cluster.
    /// </summary>
    public ServiceSpec Service { get; set; } = new();

    /// <summary>
    /// Resource requests and limits for the proxy container.
    /// </summary>
    public ResourcesSpec Resources { get; set; } = new();

    /// <summary>
    /// Message of the day displayed in the server list via the proxy.
    /// </summary>
    public string? Motd { get; set; }

    /// <summary>
    /// Maximum number of players the proxy reports in the server list.
    /// </summary>
    [Range(1, 10000)]
    public int MaxPlayers { get; set; } = 100;

    /// <summary>
    /// Whether the proxy authenticates players with Mojang.
    /// Should be true for production servers.
    /// </summary>
    public bool OnlineMode { get; set; } = true;

    /// <summary>
    /// Player info forwarding mode from the proxy to backend servers.
    /// Modern mode is recommended for Velocity and Paper servers.
    /// </summary>
    public PlayerForwardingMode PlayerForwardingMode { get; set; } = PlayerForwardingMode.Modern;

    /// <summary>
    /// Ordered list of server names (by index) that players try to connect to.
    /// When empty, servers are tried in ascending index order.
    /// Values are zero-based indices of the backend servers (e.g., [0, 1, 2]).
    /// </summary>
    public IList<int> TryOrder { get; set; } = new List<int>();

    /// <summary>
    /// Optional override for the proxy container image.
    /// Defaults to itzg/mc-proxy:latest.
    /// </summary>
    public string? Image { get; set; }

    /// <summary>
    /// Image pull policy for the proxy container. Defaults to IfNotPresent.
    /// </summary>
    public ImagePullPolicy ImagePullPolicy { get; set; } = ImagePullPolicy.IfNotPresent;

    /// <summary>
    /// The port the Velocity proxy listens on.
    /// </summary>
    [Range(1024, 65535)]
    public int ProxyPort { get; set; } = 25577;

    /// <summary>
    /// Velocity version to use. Defaults to "LATEST".
    /// </summary>
    public string Version { get; set; } = "LATEST";
}

/// <summary>
/// Player info forwarding modes supported by Velocity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayerForwardingMode
{
    /// <summary>No forwarding. Backend servers won't know the real player IP/UUID.</summary>
    None,

    /// <summary>Legacy BungeeCord-style forwarding (less secure).</summary>
    Legacy,

    /// <summary>BungeeGuard-style forwarding with token verification.</summary>
    BungeeGuard,

    /// <summary>Modern Velocity forwarding (recommended). Requires Paper or compatible backend.</summary>
    Modern,
}
