using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace McOperator.Entities;

/// <summary>
/// Represents a Minecraft server deployment managed by the operator.
/// </summary>
[KubernetesEntity(Group = "minecraft.hengeveld.dev", ApiVersion = "v1alpha1", Kind = "MinecraftServer", PluralName = "minecraftservers")]
[KubernetesEntityShortNames("mcs")]
[GenericAdditionalPrinterColumn(".status.phase", "Phase", "string", Description = "Server phase")]
[GenericAdditionalPrinterColumn(".spec.server.version", "Version", "string", Description = "Minecraft version")]
[GenericAdditionalPrinterColumn(".spec.server.type", "Type", "string", Description = "Server distribution")]
[GenericAdditionalPrinterColumn(".status.readyReplicas", "Ready", "integer", Description = "Ready replicas")]
public class MinecraftServer : CustomKubernetesEntity<MinecraftServerSpec, MinecraftServerStatus>
{
}
