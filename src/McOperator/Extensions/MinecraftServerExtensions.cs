using k8s.Models;
using McOperator.Entities;

namespace McOperator.Extensions;

/// <summary>
/// Extension methods for MinecraftServer entities to simplify common operations.
/// </summary>
public static class MinecraftServerExtensions
{
    /// <summary>
    /// Creates an owner reference from a MinecraftServer for use in child resources.
    /// </summary>
    public static V1OwnerReference MakeOwnerReference(this MinecraftServer server)
    {
        return new V1OwnerReference
        {
            ApiVersion = $"minecraft.hengeveld.dev/v1alpha1",
            Kind = "MinecraftServer",
            Name = server.Name(),
            Uid = server.Uid(),
            BlockOwnerDeletion = true,
            Controller = true,
        };
    }
}
