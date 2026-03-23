using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace McOperator.Entities;

/// <summary>
/// Represents a Minecraft server deployment managed by the operator.
/// </summary>
[KubernetesEntity(Group = OperatorConstants.Group, ApiVersion = OperatorConstants.Version,
    Kind = OperatorConstants.ServerKind, PluralName = OperatorConstants.ServerPluralName)]
[KubernetesEntityShortNames("mcs")]
[GenericAdditionalPrinterColumn(".status.phase", "Phase", "string", Description = "Server phase")]
[GenericAdditionalPrinterColumn(".spec.server.version", "Version", "string", Description = "Minecraft version")]
[GenericAdditionalPrinterColumn(".spec.server.type", "Type", "string", Description = "Server distribution")]
[GenericAdditionalPrinterColumn(".status.readyReplicas", "Ready", "integer", Description = "Ready replicas")]
public class MinecraftServer : CustomKubernetesEntity<MinecraftServerSpec, MinecraftServerStatus>
{
}
