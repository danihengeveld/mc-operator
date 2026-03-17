using k8s.Models;
using McOperator.Entities;
using McOperator.Extensions;

namespace McOperator.Builders;

/// <summary>
/// Builds Kubernetes StatefulSet manifests for MinecraftServer resources.
/// </summary>
/// <remarks>
/// StatefulSet is chosen over Deployment because:
/// - Minecraft servers are stateful singletons
/// - Stable network identity is useful for RCON and monitoring
/// - Ordered rolling updates prevent split-brain scenarios
/// - VolumeClaimTemplates integrate naturally with persistent world data
/// </remarks>
public static class StatefulSetBuilder
{
    private const string ContainerName = "minecraft";
    private const string DataVolumeName = "data";

    /// <summary>
    /// Builds a StatefulSet for the given MinecraftServer.
    /// </summary>
    public static V1StatefulSet Build(MinecraftServer server)
    {
        var name = server.Name();
        var ns = server.Namespace();
        var labels = BuildLabels(server);
        var spec = server.Spec;

        var container = BuildContainer(server);
        var podSpec = BuildPodSpec(spec, container);
        var volumeClaimTemplates = BuildVolumeClaimTemplates(server);

        return new V1StatefulSet
        {
            ApiVersion = "apps/v1",
            Kind = "StatefulSet",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = ns,
                Labels = labels,
                OwnerReferences = new List<V1OwnerReference>
                {
                    server.MakeOwnerReference(),
                },
            },
            Spec = new V1StatefulSetSpec
            {
                ServiceName = name,
                Replicas = spec.Replicas,
                Selector = new V1LabelSelector
                {
                    MatchLabels = labels,
                },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = labels,
                    },
                    Spec = podSpec,
                },
                VolumeClaimTemplates = volumeClaimTemplates,
                // Retain PVC by default - do not delete volumes on scale down
                PersistentVolumeClaimRetentionPolicy = new V1StatefulSetPersistentVolumeClaimRetentionPolicy
                {
                    WhenDeleted = spec.Storage.DeleteWithServer ? "Delete" : "Retain",
                    WhenScaled = "Retain",
                },
            },
        };
    }

    private static V1Container BuildContainer(MinecraftServer server)
    {
        var spec = server.Spec;
        var image = ResolveImage(spec);
        var env = BuildEnvironmentVariables(spec);
        var ports = new List<V1ContainerPort>
        {
            new() { Name = "minecraft", ContainerPort = spec.Properties.ServerPort, Protocol = "TCP" },
        };

        var volumeMounts = new List<V1VolumeMount>();
        if (spec.Storage.Enabled)
        {
            volumeMounts.Add(new V1VolumeMount
            {
                Name = DataVolumeName,
                MountPath = spec.Storage.MountPath,
            });
        }

        var resources = BuildResourceRequirements(spec.Resources, spec.Jvm);

        return new V1Container
        {
            Name = ContainerName,
            Image = image,
            ImagePullPolicy = spec.ImagePullPolicy.ToString(),
            Env = env,
            Ports = ports,
            VolumeMounts = volumeMounts,
            Resources = resources,
            // Readiness probe: check if the Minecraft server port is open
            ReadinessProbe = new V1Probe
            {
                TcpSocket = new V1TCPSocketAction { Port = spec.Properties.ServerPort },
                InitialDelaySeconds = 30,
                PeriodSeconds = 10,
                FailureThreshold = 6,
            },
            // Liveness probe: check the port is still open
            LivenessProbe = new V1Probe
            {
                TcpSocket = new V1TCPSocketAction { Port = spec.Properties.ServerPort },
                InitialDelaySeconds = 60,
                PeriodSeconds = 30,
                FailureThreshold = 3,
            },
        };
    }

    private static V1PodSpec BuildPodSpec(MinecraftServerSpec spec, V1Container container)
    {
        return new V1PodSpec
        {
            Containers = new List<V1Container> { container },
            // Security: run as non-root user (itzg/minecraft-server uses uid 1000)
            SecurityContext = new V1PodSecurityContext
            {
                RunAsNonRoot = true,
                RunAsUser = 1000,
                FsGroup = 1000,
            },
            TerminationGracePeriodSeconds = 60,
        };
    }

    private static IList<V1PersistentVolumeClaim>? BuildVolumeClaimTemplates(MinecraftServer server)
    {
        var spec = server.Spec;
        if (!spec.Storage.Enabled)
        {
            return null;
        }

        var pvc = new V1PersistentVolumeClaim
        {
            Metadata = new V1ObjectMeta
            {
                Name = DataVolumeName,
            },
            Spec = new V1PersistentVolumeClaimSpec
            {
                AccessModes = new List<string> { "ReadWriteOnce" },
                Resources = new V1VolumeResourceRequirements
                {
                    Requests = new Dictionary<string, ResourceQuantity>
                    {
                        ["storage"] = new ResourceQuantity(spec.Storage.Size),
                    },
                },
                StorageClassName = spec.Storage.StorageClassName,
            },
        };

        return new List<V1PersistentVolumeClaim> { pvc };
    }

    private static V1ResourceRequirements BuildResourceRequirements(ResourcesSpec resources, JvmSpec jvm)
    {
        // Determine effective memory limit; fall back to MemoryRequest if not set
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
            Requests = req.Count > 0 ? req : null,
            Limits = limits.Count > 0 ? limits : null,
        };
    }

    private static List<V1EnvVar> BuildEnvironmentVariables(MinecraftServerSpec spec)
    {
        var props = spec.Properties;
        var env = new List<V1EnvVar>
        {
            Env("EULA", spec.AcceptEula ? "TRUE" : "FALSE"),
            Env("TYPE", spec.Server.Type.ToString().ToUpperInvariant()),
            Env("VERSION", spec.Server.Version),

            // JVM memory settings
            Env("MEMORY", ""),  // disable the itzg/minecraft-server combined MEMORY env
            Env("JVM_OPTS", BuildJvmOpts(spec.Jvm)),
            Env("JVM_XX_OPTS", ""),

            // Server properties
            Env("DIFFICULTY", props.Difficulty.ToString().ToLowerInvariant()),
            Env("MODE", props.GameMode.ToString().ToLowerInvariant()),
            Env("MAX_PLAYERS", props.MaxPlayers.ToString()),
            Env("ONLINE_MODE", props.OnlineMode ? "TRUE" : "FALSE"),
            Env("ENABLE_WHITELIST", props.WhitelistEnabled ? "TRUE" : "FALSE"),
            Env("PVP", props.Pvp ? "TRUE" : "FALSE"),
            Env("HARDCORE", props.Hardcore ? "TRUE" : "FALSE"),
            Env("SPAWN_PROTECTION", props.SpawnProtection.ToString()),
            Env("LEVEL_TYPE", props.LevelType),
            Env("LEVEL", props.LevelName),
            Env("VIEW_DISTANCE", props.ViewDistance.ToString()),
            Env("SIMULATION_DISTANCE", props.SimulationDistance.ToString()),
            Env("GENERATE_STRUCTURES", props.GenerateStructures ? "TRUE" : "FALSE"),
            Env("ALLOW_NETHER", props.AllowNether ? "TRUE" : "FALSE"),
            Env("SERVER_PORT", props.ServerPort.ToString()),
        };

        if (!string.IsNullOrEmpty(props.Motd))
        {
            env.Add(Env("MOTD", props.Motd));
        }

        if (!string.IsNullOrEmpty(props.LevelSeed))
        {
            env.Add(Env("SEED", props.LevelSeed));
        }

        if (props.Ops.Count > 0)
        {
            env.Add(Env("OPS", string.Join(",", props.Ops)));
        }

        if (props.Whitelist.Count > 0)
        {
            env.Add(Env("WHITELIST", string.Join(",", props.Whitelist)));
        }

        // Additional raw properties
        foreach (var (key, value) in props.AdditionalProperties)
        {
            env.Add(Env(key, value));
        }

        return env;
    }

    private static string BuildJvmOpts(JvmSpec jvm)
    {
        var parts = new List<string>
        {
            $"-Xms{jvm.InitialMemory}",
            $"-Xmx{jvm.MaxMemory}",
        };
        parts.AddRange(jvm.ExtraJvmArgs);
        return string.Join(" ", parts);
    }

    private static V1EnvVar Env(string name, string value) =>
        new() { Name = name, Value = value };

    /// <summary>
    /// Resolves the container image for a MinecraftServer.
    /// Strategy (in priority order):
    /// 1. If spec.Image is set, use it verbatim (full user control)
    /// 2. Otherwise use the itzg/minecraft-server image, which handles all
    ///    distribution types via environment variables
    /// </summary>
    public static string ResolveImage(MinecraftServerSpec spec)
    {
        if (!string.IsNullOrEmpty(spec.Image))
        {
            return spec.Image;
        }

        // The itzg/minecraft-server image is the de facto standard for
        // containerized Minecraft servers. It supports all distributions we
        // care about (Vanilla, Paper, Spigot, Bukkit) via env vars.
        return "itzg/minecraft-server:latest";
    }

    private static IDictionary<string, string> BuildLabels(MinecraftServer server) =>
        new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = "minecraft-server",
            ["app.kubernetes.io/instance"] = server.Name(),
            ["app.kubernetes.io/managed-by"] = "mc-operator",
            ["minecraft.operator.io/server-name"] = server.Name(),
        };
}
