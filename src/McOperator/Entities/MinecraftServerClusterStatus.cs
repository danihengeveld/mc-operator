using System.Text.Json.Serialization;
using k8s.Models;

namespace McOperator.Entities;

/// <summary>
/// Observed status of a MinecraftServerCluster resource.
/// Updated by the controller; do not edit manually.
/// </summary>
public class MinecraftServerClusterStatus
{
    /// <summary>
    /// The observed generation of the cluster spec.
    /// Used to detect spec changes that have not yet been reconciled.
    /// </summary>
    public long ObservedGeneration { get; set; }

    /// <summary>
    /// The current lifecycle phase of the cluster.
    /// </summary>
    public MinecraftServerClusterPhase Phase { get; set; } = MinecraftServerClusterPhase.Pending;

    /// <summary>
    /// Human-readable message describing the current state.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Number of backend servers that are in the Running phase.
    /// </summary>
    public int ReadyServers { get; set; }

    /// <summary>
    /// Total number of backend servers managed by the cluster.
    /// </summary>
    public int TotalServers { get; set; }

    /// <summary>
    /// Number of ready proxy replicas.
    /// </summary>
    public int ReadyProxyReplicas { get; set; }

    /// <summary>
    /// Connection endpoint for the Velocity proxy.
    /// Players connect to the cluster through this endpoint.
    /// </summary>
    public EndpointStatus? ProxyEndpoint { get; set; }

    /// <summary>
    /// Status of each backend server in the cluster.
    /// </summary>
    public IList<ClusterServerStatus> Servers { get; set; } = new List<ClusterServerStatus>();

    /// <summary>
    /// Standard Kubernetes status conditions.
    /// </summary>
    public IList<V1Condition> Conditions { get; set; } = new List<V1Condition>();
}

/// <summary>
/// Lifecycle phases for a MinecraftServerCluster.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MinecraftServerClusterPhase
{
    /// <summary>Initial state, resources not yet created.</summary>
    Pending,

    /// <summary>Resources are being created or reconciled.</summary>
    Provisioning,

    /// <summary>All servers and proxy are running and ready.</summary>
    Running,

    /// <summary>Some servers are not ready, but cluster is partially operational.</summary>
    Degraded,

    /// <summary>Cluster encountered a non-recoverable error.</summary>
    Failed,

    /// <summary>Cluster resources are being torn down.</summary>
    Terminating,
}

/// <summary>
/// Status of an individual backend server in the cluster.
/// </summary>
public class ClusterServerStatus
{
    /// <summary>
    /// Name of the MinecraftServer resource.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Current lifecycle phase of the server.
    /// </summary>
    public MinecraftServerPhase Phase { get; set; }
}

/// <summary>
/// Well-known condition types for MinecraftServerCluster resources.
/// </summary>
public static class MinecraftServerClusterConditions
{
    public const string Available = "Available";
    public const string Progressing = "Progressing";
    public const string Degraded = "Degraded";
    public const string ProxyReady = "ProxyReady";
}
