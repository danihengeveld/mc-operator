using System.Text.Json.Serialization;
using k8s.Models;

namespace McOperator.Entities;

/// <summary>
/// Observed status of a MinecraftServer resource.
/// Updated by the controller; do not edit manually.
/// </summary>
public class MinecraftServerStatus
{
    /// <summary>
    /// The observed generation of the MinecraftServer spec.
    /// Used to detect spec changes that have not yet been reconciled.
    /// </summary>
    public long ObservedGeneration { get; set; }

    /// <summary>
    /// The current lifecycle phase of the server.
    /// </summary>
    public MinecraftServerPhase Phase { get; set; } = MinecraftServerPhase.Pending;

    /// <summary>
    /// Human-readable message describing the current state.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Number of ready replicas in the underlying StatefulSet.
    /// </summary>
    public int ReadyReplicas { get; set; }

    /// <summary>
    /// The container image currently in use (including tag).
    /// </summary>
    public string? CurrentImage { get; set; }

    /// <summary>
    /// The Minecraft version currently deployed.
    /// </summary>
    public string? CurrentVersion { get; set; }

    /// <summary>
    /// Connection endpoint information.
    /// </summary>
    public EndpointStatus? Endpoint { get; set; }

    /// <summary>
    /// PVC status information.
    /// </summary>
    public PvcStatus? Storage { get; set; }

    /// <summary>
    /// Standard Kubernetes status conditions.
    /// </summary>
    public IList<V1Condition> Conditions { get; set; } = new List<V1Condition>();
}

/// <summary>
/// Lifecycle phases for a MinecraftServer.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MinecraftServerPhase
{
    /// <summary>Initial state, resources not yet created.</summary>
    Pending,

    /// <summary>Resources are being created or reconciled.</summary>
    Provisioning,

    /// <summary>Server is running and ready to accept connections.</summary>
    Running,

    /// <summary>Server is paused (replicas == 0).</summary>
    Paused,

    /// <summary>Server encountered a non-recoverable error.</summary>
    Failed,

    /// <summary>Server resources are being torn down.</summary>
    Terminating,
}

/// <summary>
/// Connection endpoint details.
/// </summary>
public class EndpointStatus
{
    /// <summary>
    /// The IP address or hostname for connecting to the server.
    /// For ClusterIP services, this is the cluster-internal address.
    /// For LoadBalancer services, this is the external address once assigned.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// The port to connect to.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Full connection string (host:port).
    /// </summary>
    public string? ConnectionString => Host is not null ? $"{Host}:{Port}" : null;
}

/// <summary>
/// PVC status information.
/// </summary>
public class PvcStatus
{
    /// <summary>
    /// Name of the PVC.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Current phase of the PVC (e.g., Bound, Pending).
    /// </summary>
    public string? Phase { get; set; }

    /// <summary>
    /// Storage class in use.
    /// </summary>
    public string? StorageClassName { get; set; }

    /// <summary>
    /// Actual capacity of the bound volume.
    /// </summary>
    public string? Capacity { get; set; }
}

/// <summary>
/// Well-known condition types for MinecraftServer resources.
/// </summary>
public static class MinecraftServerConditions
{
    public const string Available = "Available";
    public const string Progressing = "Progressing";
    public const string Degraded = "Degraded";
    public const string StorageReady = "StorageReady";
}
