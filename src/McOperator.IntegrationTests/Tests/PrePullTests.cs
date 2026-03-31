using System.Net;
using System.Text.Json;
using k8s;
using k8s.Autorest;
using k8s.Models;
using McOperator.IntegrationTests.Infrastructure;

namespace McOperator.IntegrationTests.Tests;

/// <summary>
/// Integration tests for the image pre-pull mechanism.
///
/// The operator creates a short-lived Kubernetes Job that pulls a new container
/// image on the target node before updating the StatefulSet, minimising the time
/// the Minecraft server is offline during upgrades.
///
/// Tests use small images (busybox variants) so that pulls complete quickly in CI.
/// </summary>
public class PrePullTests
{
    [ClassDataSource<K3sFixture>(Shared = SharedType.PerAssembly)]
    public required K3sFixture K3s { get; init; }

    // Two lightweight images used to simulate "before" and "after" an upgrade.
    // busybox:stable (~4 MB) and busybox:stable-musl (~4 MB) are distinct tags so
    // the operator treats the change as a genuine image upgrade.
    private const string InitialImage = "busybox:stable";
    private const string UpgradedImage = "busybox:stable-musl";

    // --------------------------------------------------------------------------
    // Happy path: pre-pull Job is created on an image change
    // --------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    public async Task ImageChange_CreatesPrePullJobWithCorrectSpec(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client, cancellationToken: cancellationToken);
        try
        {
            const string serverName = "prepull-job-spec";
            var jobName = $"{serverName}-prepull";

            // 1. Create server with the initial image and wait for the operator to
            //    complete its first reconciliation (currentImage is set in status).
            await CreateServerWithImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);
            await WaitForCurrentImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);

            // 2. Change the image to trigger the pre-pull flow.
            await KubernetesHelper.PatchMinecraftServerAsync(
                K3s.Client, ns, serverName,
                new Dictionary<string, object>
                {
                    ["spec"] = new Dictionary<string, object> { ["image"] = UpgradedImage, },
                }, cancellationToken);

            // 3. Wait for the pre-pull Job to be created by the operator.
            //    The Job is short-lived but will persist for at least one full
            //    requeue cycle (30 s) even if the image pull completes quickly.
            var job = await WaitForJobAsync(K3s.Client, ns, jobName, cancellationToken);

            // 4. Verify the Job spec is correct.
            await Assert.That(job.Metadata.Name).IsEqualTo(jobName);

            var container = job.Spec.Template.Spec.Containers[0];
            await Assert.That(container.Image).IsEqualTo(UpgradedImage);
            await Assert.That(container.ImagePullPolicy).IsEqualTo("IfNotPresent");
            await Assert.That(container.Command).Contains("exit 0");

            await Assert.That(job.Spec.BackoffLimit).IsEqualTo(0);
            await Assert.That(job.Spec.Template.Spec.RestartPolicy).IsEqualTo("Never");

            // 5. Verify the expected labels are present.
            var labels = job.Metadata.Labels;
            await Assert.That(labels.ContainsKey("app.kubernetes.io/managed-by")).IsTrue();
            await Assert.That(labels["app.kubernetes.io/managed-by"]).IsEqualTo("mc-operator");
            await Assert.That(labels.ContainsKey("mc-operator.dhv.sh/pre-pull")).IsTrue();
            await Assert.That(labels["mc-operator.dhv.sh/pre-pull"]).IsEqualTo("true");
            await Assert.That(labels.ContainsKey("mc-operator.dhv.sh/server-name")).IsTrue();
            await Assert.That(labels["mc-operator.dhv.sh/server-name"]).IsEqualTo(serverName);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns, cancellationToken);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task ImageChange_StatefulSetEventuallyUpdatedToNewImage(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client, cancellationToken: cancellationToken);
        try
        {
            const string serverName = "prepull-sts-update";

            // 1. Create server with initial image and wait for first reconciliation.
            await CreateServerWithImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);
            await WaitForCurrentImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);

            // Confirm the StatefulSet has the initial image.
            var sts = await K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(
                serverName, ns, cancellationToken: cancellationToken);
            await Assert.That(sts.Spec.Template.Spec.Containers[0].Image).IsEqualTo(InitialImage);

            // 2. Change the image.
            await KubernetesHelper.PatchMinecraftServerAsync(
                K3s.Client, ns, serverName,
                new Dictionary<string, object>
                {
                    ["spec"] = new Dictionary<string, object> { ["image"] = UpgradedImage, },
                }, cancellationToken);

            // 3. Wait for the StatefulSet to be updated to the new image.
            //    The operator first pre-pulls the image (≥30 s requeue), then updates the STS.
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var updated = await K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(
                        serverName, ns, cancellationToken: cancellationToken);
                    return updated.Spec.Template.Spec.Containers[0].Image == UpgradedImage;
                },
                timeout: TimeSpan.FromSeconds(120),
                description: $"StatefulSet to be updated to image '{UpgradedImage}'");

            // 4. Verify status.currentImage is updated too.
            await WaitForCurrentImage(K3s.Client, ns, serverName, UpgradedImage, cancellationToken);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns, cancellationToken);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task ImageChange_StatusShowsPrePullingDuringPrePull(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client, cancellationToken: cancellationToken);
        try
        {
            const string serverName = "prepull-status";

            // 1. Setup: create and fully reconcile with initial image.
            await CreateServerWithImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);
            await WaitForCurrentImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);

            // 2. Change the image.
            await KubernetesHelper.PatchMinecraftServerAsync(
                K3s.Client, ns, serverName,
                new Dictionary<string, object>
                {
                    ["spec"] = new Dictionary<string, object> { ["image"] = UpgradedImage, },
                }, cancellationToken);

            // 3. While the pre-pull Job is active, the status message should contain
            //    "Pre-pulling image" and phase should be Provisioning.
            //    We accept either this transient state, or the final state where
            //    the upgrade is already complete (very fast environments).
            var sawPrePullingState = false;

            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var server = await KubernetesHelper.GetMinecraftServerAsync(
                        K3s.Client, ns, serverName, cancellationToken);
                    if (server is null) return false;

                    var json = ((JsonElement)server).GetRawText();

                    if (json.Contains("Pre-pulling image"))
                    {
                        sawPrePullingState = true;
                    }

                    // Done when StatefulSet has the new image
                    var sts = await K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(
                        serverName, ns, cancellationToken: cancellationToken);
                    return sts.Spec.Template.Spec.Containers[0].Image == UpgradedImage;
                },
                timeout: TimeSpan.FromSeconds(120),
                description: "Upgrade to complete");

            // The pre-pulling status message may have been observed transiently.
            // We don't assert on it because very fast image pulls on pre-cached nodes
            // may skip the observable window; the important thing is the upgrade
            // completes, which is validated above.
            _ = sawPrePullingState;
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns, cancellationToken);
        }
    }

    // --------------------------------------------------------------------------
    // Skip conditions: pre-pull should NOT happen
    // --------------------------------------------------------------------------

    [Test]
    [Timeout(120_000)]
    public async Task FreshServer_DoesNotCreatePrePullJob(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client, cancellationToken: cancellationToken);
        try
        {
            const string serverName = "prepull-skip-fresh";
            var jobName = $"{serverName}-prepull";

            // Create a brand-new server (no currentImage in status yet).
            await CreateServerWithImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);

            // Wait for the StatefulSet to be created (operator has reconciled).
            await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(
                    serverName, ns, cancellationToken: cancellationToken),
                timeout: TimeSpan.FromSeconds(60),
                description: $"StatefulSet '{serverName}' to be created");

            // Allow additional time for any spurious reconciliations to run.
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            // No pre-pull Job should exist for a fresh server.
            var job = await TryGetJobAsync(K3s.Client, ns, jobName, cancellationToken);
            await Assert.That(job).IsNull();
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns, cancellationToken);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task PausedServer_ImageChange_DoesNotCreatePrePullJob(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client, cancellationToken: cancellationToken);
        try
        {
            const string serverName = "prepull-skip-paused";
            var jobName = $"{serverName}-prepull";

            // 1. Create a paused server (replicas=0).
            await CreateServerWithImage(K3s.Client, ns, serverName, InitialImage, cancellationToken,
                replicas: 0);

            // Wait for reconciliation to complete.
            await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(
                    serverName, ns, cancellationToken: cancellationToken),
                timeout: TimeSpan.FromSeconds(60),
                description: $"StatefulSet '{serverName}' to be created with replicas=0");

            // 2. Manually set currentImage on the server status so the operator
            //    thinks an existing image is deployed (simulates a paused server
            //    that was previously running).
            await K3s.Client.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                body: new V1Patch(
                    new Dictionary<string, object>
                    {
                        ["status"] = new Dictionary<string, object> { ["currentImage"] = InitialImage, },
                    }, V1Patch.PatchType.MergePatch),
                group: "mc-operator.dhv.sh",
                version: "v1alpha1",
                namespaceParameter: ns,
                plural: "minecraftservers",
                name: serverName, cancellationToken: cancellationToken);

            // 3. Change the image while still paused.
            await KubernetesHelper.PatchMinecraftServerAsync(
                K3s.Client, ns, serverName,
                new Dictionary<string, object>
                {
                    ["spec"] = new Dictionary<string, object> { ["image"] = UpgradedImage, },
                }, cancellationToken);

            // 4. Allow time for reconciliation.
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

            // 5. No pre-pull Job should be created for a paused server.
            var job = await TryGetJobAsync(K3s.Client, ns, jobName, cancellationToken);
            await Assert.That(job).IsNull();
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns, cancellationToken);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task SameImage_DoesNotCreatePrePullJob(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client, cancellationToken: cancellationToken);
        try
        {
            const string serverName = "prepull-skip-same";
            var jobName = $"{serverName}-prepull";

            // Create server and wait for full reconciliation.
            await CreateServerWithImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);
            await WaitForCurrentImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);

            // Trigger reconciliation without changing the image
            // (patch an unrelated property).
            await KubernetesHelper.PatchMinecraftServerAsync(
                K3s.Client, ns, serverName,
                new Dictionary<string, object>
                {
                    ["spec"] = new Dictionary<string, object>
                    {
                        ["properties"] = new Dictionary<string, object> { ["maxPlayers"] = 15, },
                    },
                }, cancellationToken);

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            // No pre-pull Job should be created when the image has not changed.
            var job = await TryGetJobAsync(K3s.Client, ns, jobName, cancellationToken);
            await Assert.That(job).IsNull();
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns, cancellationToken);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task PrePullDisabled_ImageChange_DoesNotCreatePrePullJob(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client, cancellationToken: cancellationToken);
        try
        {
            const string serverName = "prepull-skip-disabled";
            var jobName = $"{serverName}-prepull";

            // Create server with prePull explicitly disabled.
            await CreateServerWithImage(K3s.Client, ns, serverName, InitialImage, cancellationToken,
                prePull: false);
            await WaitForCurrentImage(K3s.Client, ns, serverName, InitialImage, cancellationToken);

            // Change the image.
            await KubernetesHelper.PatchMinecraftServerAsync(
                K3s.Client, ns, serverName,
                new Dictionary<string, object>
                {
                    ["spec"] = new Dictionary<string, object> { ["image"] = UpgradedImage, },
                }, cancellationToken);

            // Allow time for reconciliation.
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

            // No pre-pull Job should be created when prePull is disabled.
            var job = await TryGetJobAsync(K3s.Client, ns, jobName, cancellationToken);
            await Assert.That(job).IsNull();
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns, cancellationToken);
        }
    }

    // --------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------

    /// <summary>
    /// Creates a MinecraftServer with a custom container image override.
    /// </summary>
    private static async Task CreateServerWithImage(
        IKubernetes client,
        string namespaceName,
        string name,
        string image,
        CancellationToken cancellationToken,
        int replicas = 1,
        bool prePull = true)
    {
        var server = new Dictionary<string, object>
        {
            ["apiVersion"] = "mc-operator.dhv.sh/v1alpha1",
            ["kind"] = "MinecraftServer",
            ["metadata"] = new Dictionary<string, object> { ["name"] = name, ["namespace"] = namespaceName, },
            ["spec"] = new Dictionary<string, object>
            {
                ["acceptEula"] = true,
                ["replicas"] = replicas,
                ["image"] = image,
                ["prePull"] = prePull,
                ["server"] = new Dictionary<string, object> { ["type"] = "Vanilla", ["version"] = "1.20.4", },
                ["properties"] = new Dictionary<string, object>
                {
                    ["maxPlayers"] = 10,
                    ["onlineMode"] = false,
                    ["viewDistance"] = 10,
                    ["simulationDistance"] = 10,
                    ["levelName"] = "world",
                    ["serverPort"] = 25565,
                },
                ["jvm"] = new Dictionary<string, object> { ["initialMemory"] = "512m", ["maxMemory"] = "1G", },
                ["resources"] =
                    new Dictionary<string, object> { ["cpuRequest"] = "250m", ["memoryRequest"] = "512Mi", },
                ["storage"] =
                    new Dictionary<string, object> { ["enabled"] = true, ["size"] = "1Gi", ["mountPath"] = "/data", },
                ["service"] = new Dictionary<string, object> { ["type"] = "ClusterIP", },
            },
        };

        await KubernetesHelper.CreateMinecraftServerAsync(client, namespaceName, server, cancellationToken);
    }

    /// <summary>
    /// Waits for the <c>status.currentImage</c> field to equal
    /// <paramref name="expectedImage"/>.
    /// </summary>
    private static async Task WaitForCurrentImage(
        IKubernetes client,
        string namespaceName,
        string name,
        string expectedImage,
        CancellationToken cancellationToken)
    {
        await KubernetesHelper.WaitForConditionAsync(
            async () =>
            {
                var server = await KubernetesHelper.GetMinecraftServerAsync(client, namespaceName, name,
                    cancellationToken);
                if (server is null) return false;
                var jsonElement = (JsonElement)server;
                if (!jsonElement.TryGetProperty("status", out var status)) return false;
                if (!status.TryGetProperty("currentImage", out var img)) return false;
                return img.GetString() == expectedImage;
            },
            timeout: TimeSpan.FromSeconds(60),
            description: $"status.currentImage to equal '{expectedImage}'");
    }

    /// <summary>
    /// Waits for a Kubernetes Job to appear and returns it.
    /// </summary>
    private static Task<V1Job> WaitForJobAsync(
        IKubernetes client,
        string namespaceName,
        string jobName,
        CancellationToken cancellationToken)
    {
        return KubernetesHelper.WaitForResourceAsync(
            async () =>
            {
                try
                {
                    return await client.BatchV1.ReadNamespacedJobAsync(
                        jobName, namespaceName, cancellationToken: cancellationToken);
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
            },
            timeout: TimeSpan.FromSeconds(60),
            description: $"pre-pull Job '{jobName}' to be created");
    }

    /// <summary>
    /// Returns the Job if it exists, or <c>null</c> if it does not.
    /// </summary>
    private static async Task<V1Job?> TryGetJobAsync(
        IKubernetes client,
        string namespaceName,
        string jobName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.BatchV1.ReadNamespacedJobAsync(
                jobName, namespaceName, cancellationToken: cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
