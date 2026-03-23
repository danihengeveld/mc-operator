using k8s.Models;
using McOperator.Entities;
using McOperator.Extensions;

namespace McOperator.Builders;

/// <summary>
/// Builds a short-lived Kubernetes Job whose sole purpose is to pull a container
/// image onto a specific node before the Minecraft server pod is restarted.
/// The job exits immediately after Kubernetes has pulled the image, so the
/// StatefulSet rolling-update can then start the server without waiting for a
/// potentially large image download.
/// </summary>
public static class PrePullJobBuilder
{
    private const string ContainerName = "prepull";

    /// <summary>
    /// Returns the deterministic name used for the pre-pull Job of the given server.
    /// </summary>
    public static string JobName(MinecraftServer server) => $"{server.Name()}-prepull";

    /// <summary>
    /// Builds a one-shot Job that pulls <paramref name="image"/> on
    /// <paramref name="nodeName"/> (or any available node when <c>null</c>)
    /// and exits with code 0.
    /// </summary>
    /// <param name="server">The owner MinecraftServer resource.</param>
    /// <param name="image">The container image to pre-pull.</param>
    /// <param name="nodeName">
    /// The Kubernetes node to target.  Pass <c>null</c> to allow the scheduler
    /// to pick any node (less optimal but safe as a fallback).
    /// </param>
    public static V1Job Build(MinecraftServer server, string image, string? nodeName)
    {
        var name = JobName(server);
        var labels = BuildLabels(server);

        var container = new V1Container
        {
            Name = ContainerName,
            Image = image,
            // IfNotPresent is correct here: pre-pull is only triggered when the
            // image string changes (a different tag), so the new tag will almost
            // certainly not be cached on the target node.  If it already is cached
            // (e.g., a previous failed upgrade), skipping the re-pull is fine.
            ImagePullPolicy = "IfNotPresent",
            // Override the image entrypoint so the server startup script never runs.
            Command = ["sh", "-c", "exit 0"],
            Resources = new V1ResourceRequirements
            {
                Requests = new Dictionary<string, ResourceQuantity>
                {
                    ["cpu"] = new("10m"),
                    ["memory"] = new("16Mi"),
                },
            },
        };

        var podSpec = new V1PodSpec
        {
            RestartPolicy = "Never",
            Containers = [container],
            // Run as the same non-root UID used by the server container so
            // the job works with images that enforce non-root security policies.
            SecurityContext = new V1PodSecurityContext { RunAsNonRoot = true, RunAsUser = 1000 },
        };

        if (!string.IsNullOrEmpty(nodeName))
        {
            podSpec.NodeName = nodeName;
        }

        return new V1Job
        {
            ApiVersion = "batch/v1",
            Kind = "Job",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = server.Namespace(),
                Labels = labels,
                // Owned by the MinecraftServer so it is garbage-collected if the
                // server is deleted while a pre-pull is in progress.
                OwnerReferences = [server.MakeOwnerReference()],
            },
            Spec = new V1JobSpec
            {
                // No retries: a failed pull means something is wrong with the image.
                // The controller will notice the failure and proceed with the upgrade
                // anyway rather than blocking indefinitely.
                BackoffLimit = 0,
                // Abort if the image pull takes more than 10 minutes.
                ActiveDeadlineSeconds = 600,
                // Auto-delete the finished job after 5 minutes to keep the cluster tidy.
                TtlSecondsAfterFinished = 300,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels },
                    Spec = podSpec,
                },
            },
        };
    }

    private static Dictionary<string, string> BuildLabels(MinecraftServer server) =>
        new()
        {
            [OperatorConstants.AppManagedByLabel] = OperatorConstants.OperatorName,
            [OperatorConstants.ServerNameLabel] = server.Name(),
            [OperatorConstants.PrePullLabel] = "true",
        };
}
