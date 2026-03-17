using KubeOps.Operator.Web.Webhooks.Admission.Mutation;
using McOperator.Entities;

namespace McOperator.Webhooks;

/// <summary>
/// Mutation webhook for MinecraftServer resources.
/// Applies sensible defaults when not specified by the user.
/// This ensures the spec is always in a normalized, complete state
/// before the controller reconciles it.
/// </summary>
[MutationWebhook(typeof(MinecraftServer))]
public class MinecraftServerMutationWebhook : MutationWebhook<MinecraftServer>
{
    public override MutationResult<MinecraftServer> Create(MinecraftServer server, bool dryRun)
    {
        return ApplyDefaults(this, server);
    }

    public override MutationResult<MinecraftServer> Update(MinecraftServer newServer, MinecraftServer oldServer,
        bool dryRun)
    {
        return ApplyDefaults(this, newServer);
    }

    /// <summary>
    /// Applies and normalizes defaults for the MinecraftServer spec.
    /// </summary>
    private static MutationResult<MinecraftServer> ApplyDefaults(
        MinecraftServerMutationWebhook hook,
        MinecraftServer server)
    {
        var modified = false;

        // Normalize version - trim and uppercase
        if (string.IsNullOrWhiteSpace(server.Spec.Server.Version))
        {
            server.Spec.Server.Version = "LATEST";
            modified = true;
        }

        // Normalize MOTD - trim whitespace
        if (server.Spec.Properties.Motd is not null)
        {
            var trimmed = server.Spec.Properties.Motd.Trim();
            if (trimmed != server.Spec.Properties.Motd)
            {
                server.Spec.Properties.Motd = trimmed;
                modified = true;
            }
        }

        // Default MOTD if not provided
        if (string.IsNullOrEmpty(server.Spec.Properties.Motd))
        {
            server.Spec.Properties.Motd = $"A Minecraft Server ({server.Spec.Server.Type})";
            modified = true;
        }

        // Normalize LevelType to uppercase
        if (!string.IsNullOrEmpty(server.Spec.Properties.LevelType))
        {
            var upperLevelType = server.Spec.Properties.LevelType.ToUpperInvariant();
            if (upperLevelType != server.Spec.Properties.LevelType)
            {
                server.Spec.Properties.LevelType = upperLevelType;
                modified = true;
            }
        }

        if (!modified)
        {
            return hook.NoChanges();
        }

        return hook.Modified(server);
    }
}
