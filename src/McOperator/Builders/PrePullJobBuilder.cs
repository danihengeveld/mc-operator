using k8s.Models;
using McOperator.Entities;
using McOperator.Extensions;

namespace McOperator.Builders;

/// <summary>
/// Builds a short-lived Kubernetes Job that pre-pulls a container image and,
/// when using the default itzg/minecraft-server image, also pre-downloads the
/// Minecraft server jar onto the data PVC before the rolling update begins.
/// </summary>
/// <remarks>
/// Two distinct modes are supported:
/// <list type="bullet">
///   <item><description>
///     <b>Jar-download mode</b> (default itzg image + storage enabled):
///     the container mounts the server's data PVC and runs the itzg startup
///     scripts with a lightweight fake <c>java</c> stub placed at the front of
///     <c>PATH</c> (and via <c>JAVA_CMD</c>).  The startup scripts resolve the
///     correct Minecraft version, download the server jar to the PVC mount, and
///     then try to launch the JVM — at which point the stub exits 0, completing
///     the job cleanly.  <c>SKIP_SERVER_PROPERTIES=true</c> prevents the startup
///     from overwriting any config files that the live server is using.
///   </description></item>
///   <item><description>
///     <b>OCI-only mode</b> (custom image or storage disabled):
///     the container exits immediately after the OCI image layers are pulled onto
///     the node, which is the original behaviour.
///   </description></item>
/// </list>
/// </remarks>
public static class PrePullJobBuilder
{
    private const string ContainerName = "prepull";
    private const string DataVolumeName = "data";
    // Must match the UID/GID used by the itzg/minecraft-server image and the StatefulSet.
    private const long ServerUid = 1000;
    // Shell command that creates a lightweight fake `java` binary and then runs
    // the itzg start script.  The stub exits 0 immediately, so the itzg startup
    // sequence downloads the server jar but the Minecraft JVM is never launched.
    // Both PATH and JAVA_CMD are overridden so the stub is found regardless of
    // whether the itzg scripts invoke `java` via PATH or via $JAVA_CMD.
    private const string FakeJvmStartCommand =
        "mkdir -p /tmp/fj && " +
        "printf '#!/bin/sh\\nexit 0\\n' > /tmp/fj/java && " +
        "chmod +x /tmp/fj/java && " +
        "export PATH=/tmp/fj:$PATH && " +
        "export JAVA_CMD=/tmp/fj/java && " +
        "exec /start";

    /// <summary>
    /// Returns the deterministic name used for the pre-pull Job of the given server.
    /// </summary>
    public static string JobName(MinecraftServer server) => $"{server.Name()}-prepull";

    /// <summary>
    /// Builds a one-shot Job that pulls <paramref name="image"/> on
    /// <paramref name="nodeName"/> (or any available node when <c>null</c>).
    /// When the server uses the default itzg image and has persistent storage, the
    /// Job also pre-downloads the Minecraft server jar to the data PVC so the
    /// rolling update only waits for the server process to start, not for a jar
    /// download.
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
        var spec = server.Spec;

        // Jar pre-download is only possible when using the default itzg image
        // (a custom image override means we cannot assume the itzg startup
        // script structure or jar download location) and when storage is enabled
        // (the jar must be written to the server's data PVC).
        var isItzgImage = string.IsNullOrEmpty(spec.Image);
        var shouldDownloadJar = isItzgImage && spec.Storage.Enabled;

        var container = shouldDownloadJar
            ? BuildJarDownloadContainer(spec, image)
            : BuildOciOnlyContainer(image);

        var podSpec = new V1PodSpec
        {
            RestartPolicy = "Never",
            Containers = [container],
            // Run as the same non-root UID/GID used by the server container so
            // the job works with images that enforce non-root security policies
            // and so that any files written to the PVC have the correct ownership.
            SecurityContext = new V1PodSecurityContext
            {
                RunAsNonRoot = true,
                RunAsUser = ServerUid,
                FsGroup = shouldDownloadJar ? ServerUid : null,
            },
        };

        if (shouldDownloadJar)
        {
            // Mount the server's data PVC so the jar download writes to the
            // same persistent volume that the Minecraft server pod uses.
            // The PVC name follows the StatefulSet convention:
            //   <volumeName>-<statefulSetName>-<ordinal>
            podSpec.Volumes =
            [
                new V1Volume
                {
                    Name = DataVolumeName,
                    PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                    {
                        ClaimName = $"{DataVolumeName}-{server.Name()}-0",
                    },
                },
            ];
        }

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
                // Abort if the image pull or jar download takes more than 10 minutes.
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

    /// <summary>
    /// Container that only pulls the OCI image layers and exits immediately.
    /// Used for custom (non-itzg) images where the startup script structure
    /// and jar download path are unknown.
    /// </summary>
    private static V1Container BuildOciOnlyContainer(string image) =>
        new()
        {
            Name = ContainerName,
            Image = image,
            ImagePullPolicy = "IfNotPresent",
            // Override the entrypoint so no server logic runs; Kubernetes will
            // still pull all required image layers before executing this.
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

    /// <summary>
    /// Container that runs the itzg startup scripts up to (but not including)
    /// the JVM invocation, writing the Minecraft server jar to the mounted PVC.
    /// </summary>
    /// <remarks>
    /// A lightweight <c>java</c> stub is created at <c>/tmp/fj/java</c> and
    /// placed ahead of the real JVM in both <c>PATH</c> and <c>JAVA_CMD</c>.
    /// When the startup sequence eventually tries to launch the server with
    /// <c>exec $JAVA_CMD ...</c> or <c>exec java ...</c>, the stub exits 0,
    /// completing the job cleanly without ever starting a Minecraft process.
    /// All earlier steps — version resolution, jar download, etc. — have already
    /// run against the mounted <c>/data</c> volume at that point.
    /// </remarks>
    private static V1Container BuildJarDownloadContainer(MinecraftServerSpec spec, string image) =>
        new()
        {
            Name = ContainerName,
            Image = image,
            ImagePullPolicy = "IfNotPresent",
            // Set up a fake JVM that exits 0 so the itzg startup scripts run
            // normally (downloading the server jar) but the Minecraft server
            // process itself is never started.
            Command =
            [
                "sh", "-c",
                FakeJvmStartCommand,
            ],
            Env =
            [
                // Required by itzg to accept the Minecraft EULA.
                new V1EnvVar { Name = "EULA", Value = "TRUE" },
                // Tell itzg which server distribution and version to download.
                new V1EnvVar { Name = "TYPE", Value = spec.Server.Type.ToString().ToUpperInvariant() },
                new V1EnvVar { Name = "VERSION", Value = spec.Server.Version },
                // Prevent the startup scripts from overwriting server.properties
                // and other config files that the live server is actively using.
                new V1EnvVar { Name = "SKIP_SERVER_PROPERTIES", Value = "true" },
            ],
            VolumeMounts =
            [
                new V1VolumeMount
                {
                    Name = DataVolumeName,
                    MountPath = spec.Storage.MountPath,
                },
            ],
            WorkingDir = spec.Storage.MountPath,
            // More headroom than the OCI-only container because mc-image-helper
            // and the itzg shell scripts run briefly before the fake JVM exits.
            Resources = new V1ResourceRequirements
            {
                Requests = new Dictionary<string, ResourceQuantity>
                {
                    ["cpu"] = new("50m"),
                    ["memory"] = new("128Mi"),
                },
            },
        };

    private static Dictionary<string, string> BuildLabels(MinecraftServer server) =>
        new()
        {
            [OperatorConstants.AppManagedByLabel] = OperatorConstants.OperatorName,
            [OperatorConstants.ServerNameLabel] = server.Name(),
            [OperatorConstants.PrePullLabel] = "true",
        };
}
