using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace McOperator.Entities;

/// <summary>
/// Spec for a MinecraftServer resource. All server configuration lives here.
/// </summary>
public class MinecraftServerSpec
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
    /// Resource requests and limits for the server container.
    /// </summary>
    public ResourcesSpec Resources { get; set; } = new();

    /// <summary>
    /// Persistent storage configuration.
    /// </summary>
    public StorageSpec Storage { get; set; } = new();

    /// <summary>
    /// Kubernetes Service exposure settings.
    /// </summary>
    public ServiceSpec Service { get; set; } = new();

    /// <summary>
    /// Whether to accept the Minecraft EULA. Must be true to start the server.
    /// </summary>
    [Required]
    public bool AcceptEula { get; set; } = false;

    /// <summary>
    /// Optional override for the container image.
    /// If not set, the image is derived from the server type and distribution strategy.
    /// </summary>
    public string? Image { get; set; }

    /// <summary>
    /// Image pull policy. Defaults to IfNotPresent.
    /// </summary>
    public ImagePullPolicy ImagePullPolicy { get; set; } = ImagePullPolicy.IfNotPresent;

    /// <summary>
    /// Number of replicas. Must be 0 or 1. Minecraft servers are stateful singletons.
    /// Set to 0 to pause the server without deleting data.
    /// </summary>
    [Range(0, 1)]
    public int Replicas { get; set; } = 1;
}

/// <summary>
/// Server distribution and version configuration.
/// </summary>
public class ServerSpec
{
    /// <summary>
    /// The Minecraft server distribution type.
    /// </summary>
    [Required]
    public ServerType Type { get; set; } = ServerType.Vanilla;

    /// <summary>
    /// The Minecraft version to run (e.g. "1.20.4", "LATEST").
    /// Defaults to "LATEST".
    /// </summary>
    public string Version { get; set; } = "LATEST";
}

/// <summary>
/// Supported Minecraft server distributions.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServerType
{
    Vanilla,
    Paper,
    Spigot,
    Bukkit,
}

/// <summary>
/// Minecraft server gameplay properties (maps to server.properties).
/// All fields are optional; defaults match vanilla Minecraft defaults unless noted.
/// </summary>
public class ServerPropertiesSpec
{
    /// <summary>
    /// Message of the day displayed in the server list.
    /// </summary>
    public string? Motd { get; set; }

    /// <summary>
    /// Server difficulty. Defaults to Easy.
    /// </summary>
    public Difficulty Difficulty { get; set; } = Difficulty.Easy;

    /// <summary>
    /// Default game mode for new players.
    /// </summary>
    public GameMode GameMode { get; set; } = GameMode.Survival;

    /// <summary>
    /// Maximum number of players allowed simultaneously.
    /// </summary>
    [Range(1, 10000)]
    public int MaxPlayers { get; set; } = 20;

    /// <summary>
    /// Whether the server runs in online mode (authenticates with Mojang).
    /// Set to false for offline/cracked mode. Defaults to true.
    /// </summary>
    public bool OnlineMode { get; set; } = true;

    /// <summary>
    /// Whether the whitelist is enabled. When true, only whitelisted players can join.
    /// </summary>
    public bool WhitelistEnabled { get; set; } = false;

    /// <summary>
    /// Players to add to the whitelist (player usernames or UUIDs).
    /// </summary>
    public IList<string> Whitelist { get; set; } = new List<string>();

    /// <summary>
    /// Players to grant operator (OP) status.
    /// </summary>
    public IList<string> Ops { get; set; } = new List<string>();

    /// <summary>
    /// Whether PvP (player vs player) combat is enabled.
    /// </summary>
    public bool Pvp { get; set; } = true;

    /// <summary>
    /// Whether the server runs in hardcore mode (one life per player).
    /// </summary>
    public bool Hardcore { get; set; } = false;

    /// <summary>
    /// Spawn protection radius in blocks. 0 to disable.
    /// </summary>
    [Range(0, 1000)]
    public int SpawnProtection { get; set; } = 16;

    /// <summary>
    /// World seed. Empty string means a random seed.
    /// </summary>
    public string? LevelSeed { get; set; }

    /// <summary>
    /// World type (DEFAULT, FLAT, LARGEBIOMES, AMPLIFIED, etc.).
    /// </summary>
    public string LevelType { get; set; } = "DEFAULT";

    /// <summary>
    /// Name of the world directory.
    /// </summary>
    public string LevelName { get; set; } = "world";

    /// <summary>
    /// View distance in chunks.
    /// </summary>
    [Range(3, 32)]
    public int ViewDistance { get; set; } = 10;

    /// <summary>
    /// Simulation distance in chunks.
    /// </summary>
    [Range(3, 32)]
    public int SimulationDistance { get; set; } = 10;

    /// <summary>
    /// Whether to generate structures (villages, strongholds, etc.).
    /// </summary>
    public bool GenerateStructures { get; set; } = true;

    /// <summary>
    /// Whether the Nether dimension is enabled.
    /// </summary>
    public bool AllowNether { get; set; } = true;

    /// <summary>
    /// The port the Minecraft server listens on inside the container.
    /// </summary>
    [Range(1024, 65535)]
    public int ServerPort { get; set; } = 25565;

    /// <summary>
    /// Additional raw server.properties lines (key=value format).
    /// These override any previously derived properties.
    /// </summary>
    public IDictionary<string, string> AdditionalProperties { get; set; } = new Dictionary<string, string>();
}

/// <summary>Minecraft server difficulty levels.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Difficulty
{
    Peaceful,
    Easy,
    Normal,
    Hard,
}

/// <summary>Minecraft game modes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameMode
{
    Survival,
    Creative,
    Adventure,
    Spectator,
}

/// <summary>
/// JVM configuration for the Minecraft server process.
/// </summary>
public class JvmSpec
{
    /// <summary>
    /// Initial heap size (e.g. "512m", "1G"). Maps to -Xms.
    /// Defaults to "512m".
    /// </summary>
    public string InitialMemory { get; set; } = "512m";

    /// <summary>
    /// Maximum heap size (e.g. "1G", "2G"). Maps to -Xmx.
    /// Defaults to "1G".
    /// </summary>
    public string MaxMemory { get; set; } = "1G";

    /// <summary>
    /// Additional JVM flags to pass to the server process.
    /// </summary>
    public IList<string> ExtraJvmArgs { get; set; } = new List<string>();
}

/// <summary>
/// Kubernetes resource requests and limits for the server container.
/// </summary>
public class ResourcesSpec
{
    /// <summary>
    /// CPU request (e.g. "500m", "1"). Defaults to "500m".
    /// </summary>
    public string? CpuRequest { get; set; } = "500m";

    /// <summary>
    /// CPU limit (e.g. "2", "4"). Optional; no limit by default.
    /// </summary>
    public string? CpuLimit { get; set; }

    /// <summary>
    /// Memory request (e.g. "1Gi", "2Gi"). Defaults to "1Gi".
    /// </summary>
    public string? MemoryRequest { get; set; } = "1Gi";

    /// <summary>
    /// Memory limit (e.g. "2Gi", "4Gi"). Optional; if not set, defaults to MemoryRequest.
    /// </summary>
    public string? MemoryLimit { get; set; }
}

/// <summary>
/// Persistent storage configuration for the Minecraft world and server data.
/// </summary>
public class StorageSpec
{
    /// <summary>
    /// Whether to create and manage a PersistentVolumeClaim for server data.
    /// Defaults to true. Set to false only for ephemeral/testing deployments.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The storage size to request (e.g. "10Gi", "50Gi"). Defaults to "10Gi".
    /// </summary>
    public string Size { get; set; } = "10Gi";

    /// <summary>
    /// The StorageClass to use. If not set, the cluster default StorageClass is used.
    /// </summary>
    public string? StorageClassName { get; set; }

    /// <summary>
    /// Whether the PVC should be deleted when the MinecraftServer resource is deleted.
    /// Defaults to false (Retain) to prevent accidental data loss.
    /// </summary>
    public bool DeleteWithServer { get; set; } = false;

    /// <summary>
    /// The path inside the container where server data is mounted.
    /// Defaults to "/data".
    /// </summary>
    public string MountPath { get; set; } = "/data";
}

/// <summary>
/// Kubernetes Service configuration for exposing the Minecraft server.
/// </summary>
public class ServiceSpec
{
    /// <summary>
    /// The Kubernetes Service type. Defaults to ClusterIP.
    /// Use NodePort or LoadBalancer to allow external connections.
    /// </summary>
    public ServiceType Type { get; set; } = ServiceType.ClusterIP;

    /// <summary>
    /// The NodePort to use when ServiceType is NodePort. Leave null for auto-assignment.
    /// </summary>
    [Range(30000, 32767)]
    public int? NodePort { get; set; }

    /// <summary>
    /// Additional annotations to add to the Service resource.
    /// Useful for cloud provider LoadBalancer configuration.
    /// </summary>
    public IDictionary<string, string> Annotations { get; set; } = new Dictionary<string, string>();
}

/// <summary>Kubernetes Service types supported by the operator.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceType
{
    ClusterIP,
    NodePort,
    LoadBalancer,
}

/// <summary>Container image pull policies.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImagePullPolicy
{
    Always,
    IfNotPresent,
    Never,
}
