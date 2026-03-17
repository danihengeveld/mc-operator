using k8s.Models;
using McOperator.Entities;
using McOperator.Extensions;

namespace McOperator.Builders;

/// <summary>
/// Builds Kubernetes ConfigMap manifests for MinecraftServer resources.
/// The ConfigMap contains a complete server.properties file and supplementary config,
/// which is mounted into the container. This is a secondary configuration mechanism;
/// the primary mechanism is environment variables (handled by itzg/minecraft-server).
/// The ConfigMap is primarily useful for audit/visibility purposes and future extensions.
/// </summary>
public static class ConfigMapBuilder
{
    /// <summary>
    /// Builds a ConfigMap containing the derived server configuration for a MinecraftServer.
    /// </summary>
    public static V1ConfigMap Build(MinecraftServer server)
    {
        var name = ConfigMapName(server.Name());
        var ns = server.Namespace();
        var spec = server.Spec;
        var labels = BuildLabels(server);

        var data = new Dictionary<string, string>
        {
            ["server-type"] = spec.Server.Type.ToString(),
            ["server-version"] = spec.Server.Version,
            ["image"] = StatefulSetBuilder.ResolveImage(spec),
            ["server.properties"] = BuildServerPropertiesContent(spec.Properties),
        };

        return new V1ConfigMap
        {
            ApiVersion = "v1",
            Kind = "ConfigMap",
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
            Data = data,
        };
    }

    /// <summary>
    /// Returns the ConfigMap name for a given server name.
    /// </summary>
    public static string ConfigMapName(string serverName) => $"{serverName}-config";

    /// <summary>
    /// Generates the server.properties file content from the spec.
    /// This is informational; the itzg image uses env vars to manage properties.
    /// </summary>
    public static string BuildServerPropertiesContent(ServerPropertiesSpec props)
    {
        var lines = new List<string>
        {
            "# Managed by mc-operator - do not edit manually",
            $"difficulty={props.Difficulty.ToString().ToLowerInvariant()}",
            $"gamemode={props.GameMode.ToString().ToLowerInvariant()}",
            $"max-players={props.MaxPlayers}",
            $"online-mode={props.OnlineMode.ToString().ToLowerInvariant()}",
            $"enable-whitelist={props.WhitelistEnabled.ToString().ToLowerInvariant()}",
            $"pvp={props.Pvp.ToString().ToLowerInvariant()}",
            $"hardcore={props.Hardcore.ToString().ToLowerInvariant()}",
            $"spawn-protection={props.SpawnProtection}",
            $"level-type={props.LevelType}",
            $"level-name={props.LevelName}",
            $"view-distance={props.ViewDistance}",
            $"simulation-distance={props.SimulationDistance}",
            $"generate-structures={props.GenerateStructures.ToString().ToLowerInvariant()}",
            $"allow-nether={props.AllowNether.ToString().ToLowerInvariant()}",
            $"server-port={props.ServerPort}",
        };

        if (!string.IsNullOrEmpty(props.Motd))
        {
            lines.Add($"motd={props.Motd}");
        }

        if (!string.IsNullOrEmpty(props.LevelSeed))
        {
            lines.Add($"level-seed={props.LevelSeed}");
        }

        // Additional custom properties
        foreach (var (key, value) in props.AdditionalProperties)
        {
            lines.Add($"{key}={value}");
        }

        return string.Join("\n", lines);
    }

    private static IDictionary<string, string> BuildLabels(MinecraftServer server) =>
        new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = "minecraft-server",
            ["app.kubernetes.io/instance"] = server.Name(),
            ["app.kubernetes.io/managed-by"] = "mc-operator",
            ["minecraft.operator.io/server-name"] = server.Name(),
            ["minecraft.operator.io/config"] = "true",
        };
}
