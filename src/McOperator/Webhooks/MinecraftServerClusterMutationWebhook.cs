using KubeOps.Operator.Web.Webhooks.Admission.Mutation;
using McOperator.Entities;

namespace McOperator.Webhooks;

/// <summary>
/// Mutation webhook for MinecraftServerCluster resources.
/// Applies sensible defaults when not specified by the user.
/// </summary>
[MutationWebhook(typeof(MinecraftServerCluster))]
public class MinecraftServerClusterMutationWebhook : MutationWebhook<MinecraftServerCluster>
{
    public override MutationResult<MinecraftServerCluster> Create(MinecraftServerCluster cluster, bool dryRun)
    {
        return ApplyDefaults(this, cluster);
    }

    public override MutationResult<MinecraftServerCluster> Update(
        MinecraftServerCluster newCluster,
        MinecraftServerCluster oldCluster,
        bool dryRun)
    {
        return ApplyDefaults(this, newCluster);
    }

    private static MutationResult<MinecraftServerCluster> ApplyDefaults(
        MinecraftServerClusterMutationWebhook hook,
        MinecraftServerCluster cluster)
    {
        var modified = false;

        // Normalize server version
        if (string.IsNullOrWhiteSpace(cluster.Spec.Template.Server.Version))
        {
            cluster.Spec.Template.Server.Version = "LATEST";
            modified = true;
        }

        // Default proxy MOTD
        if (string.IsNullOrEmpty(cluster.Spec.Proxy.Motd))
        {
            cluster.Spec.Proxy.Motd = $"A Minecraft Cluster ({cluster.Spec.Template.Server.Type})";
            modified = true;
        }
        else
        {
            var trimmed = cluster.Spec.Proxy.Motd.Trim();
            if (trimmed != cluster.Spec.Proxy.Motd)
            {
                cluster.Spec.Proxy.Motd = trimmed;
                modified = true;
            }
        }

        // Default template MOTD
        if (string.IsNullOrEmpty(cluster.Spec.Template.Properties.Motd))
        {
            cluster.Spec.Template.Properties.Motd =
                $"A Minecraft Server ({cluster.Spec.Template.Server.Type})";
            modified = true;
        }

        // Normalize LevelType to uppercase
        if (!string.IsNullOrEmpty(cluster.Spec.Template.Properties.LevelType))
        {
            var upperLevelType = cluster.Spec.Template.Properties.LevelType.ToUpperInvariant();
            if (upperLevelType != cluster.Spec.Template.Properties.LevelType)
            {
                cluster.Spec.Template.Properties.LevelType = upperLevelType;
                modified = true;
            }
        }

        if (!modified)
        {
            return hook.NoChanges();
        }

        return hook.Modified(cluster);
    }
}
