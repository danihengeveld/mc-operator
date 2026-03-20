using k8s.Models;
using McOperator.Entities;

namespace McOperator.Extensions;

/// <summary>
/// Extension methods for MinecraftServerCluster entities to simplify common operations.
/// </summary>
public static class MinecraftServerClusterExtensions
{
    /// <summary>
    /// Creates an owner reference from a MinecraftServerCluster for use in child resources.
    /// </summary>
    public static V1OwnerReference MakeOwnerReference(this MinecraftServerCluster cluster)
    {
        return new V1OwnerReference
        {
            ApiVersion = "mc-operator.dhv.sh/v1alpha1",
            Kind = "MinecraftServerCluster",
            Name = cluster.Name(),
            Uid = cluster.Uid(),
            BlockOwnerDeletion = true,
            Controller = true,
        };
    }

    /// <summary>
    /// Returns the name of the backend MinecraftServer for a given index.
    /// </summary>
    public static string ServerName(this MinecraftServerCluster cluster, int index)
    {
        return $"{cluster.Name()}-{index}";
    }

    /// <summary>
    /// Returns the name of the Velocity proxy Deployment.
    /// </summary>
    public static string ProxyName(this MinecraftServerCluster cluster)
    {
        return $"{cluster.Name()}-proxy";
    }

    /// <summary>
    /// Returns the name of the Velocity proxy ConfigMap.
    /// </summary>
    public static string ProxyConfigMapName(this MinecraftServerCluster cluster)
    {
        return $"{cluster.Name()}-proxy-config";
    }

    /// <summary>
    /// Returns the name of the Velocity proxy Service.
    /// </summary>
    public static string ProxyServiceName(this MinecraftServerCluster cluster)
    {
        return $"{cluster.Name()}-proxy";
    }

    /// <summary>
    /// Converts a MinecraftServerTemplate into a MinecraftServerSpec.
    /// </summary>
    public static MinecraftServerSpec ToMinecraftServerSpec(this MinecraftServerTemplate template)
    {
        return new MinecraftServerSpec
        {
            Server = template.Server,
            Properties = template.Properties,
            Jvm = template.Jvm,
            Resources = template.Resources,
            Storage = template.Storage,
            AcceptEula = template.AcceptEula,
            Image = template.Image,
            ImagePullPolicy = template.ImagePullPolicy,
            // Backend servers always run as single replicas managed by their StatefulSet
            Replicas = 1,
        };
    }
}
