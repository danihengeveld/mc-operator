using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace McOperator.Entities;

/// <summary>
/// Represents a cluster of Minecraft servers behind a Velocity proxy, managed by the operator.
/// The cluster automatically deploys a Velocity proxy and registers/deregisters backend
/// MinecraftServer instances as they scale.
/// </summary>
[KubernetesEntity(Group = OperatorConstants.Group, ApiVersion = OperatorConstants.Version,
    Kind = OperatorConstants.ClusterKind, PluralName = OperatorConstants.ClusterPluralName)]
[KubernetesEntityShortNames("mcsc")]
[GenericAdditionalPrinterColumn(".status.phase", "Phase", "string", Description = "Cluster phase")]
[GenericAdditionalPrinterColumn(".status.readyServers", "Ready", "integer",
    Description = "Ready backend servers")]
[GenericAdditionalPrinterColumn(".status.totalServers", "Total", "integer",
    Description = "Total backend servers")]
[GenericAdditionalPrinterColumn(".spec.template.server.version", "Version", "string",
    Description = "Minecraft version")]
public class MinecraftServerCluster : CustomKubernetesEntity<MinecraftServerClusterSpec, MinecraftServerClusterStatus>
{
}
