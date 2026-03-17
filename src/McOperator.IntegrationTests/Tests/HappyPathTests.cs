using System.Text.Json;
using k8s;
using McOperator.IntegrationTests.Infrastructure;
using TUnit.Assertions.Extensions;

namespace McOperator.IntegrationTests.Tests;

/// <summary>
/// Tests that creating a valid MinecraftServer CR triggers reconciliation
/// and results in the expected child resources being created.
/// </summary>
public class HappyPathTests
{
    [ClassDataSource<K3sFixture>(Shared = SharedType.PerAssembly)]
    public required K3sFixture K3s { get; init; }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidServer_CreatesStatefulSet(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "happy-sts";
            await CreateValidMinecraftServer(K3s.Client, ns, serverName);

            // Wait for the StatefulSet to be created by the operator
            var sts = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(serverName, ns),
                timeout: TimeSpan.FromSeconds(60),
                description: $"StatefulSet '{serverName}' to be created");

            await Assert.That(sts).IsNotNull();
            await Assert.That(sts.Metadata.Name).IsEqualTo(serverName);
            await Assert.That(sts.Spec.Replicas).IsEqualTo(1);
            await Assert.That(sts.Spec.Template.Spec.Containers).Count().IsEqualTo(1);

            // Verify the container uses the expected image
            var container = sts.Spec.Template.Spec.Containers[0];
            await Assert.That(container.Image).IsEqualTo("itzg/minecraft-server:latest");
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidServer_CreatesService(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "happy-svc";
            await CreateValidMinecraftServer(K3s.Client, ns, serverName);

            // Wait for the Service to be created by the operator
            var svc = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.CoreV1.ReadNamespacedServiceAsync(serverName, ns),
                timeout: TimeSpan.FromSeconds(60),
                description: $"Service '{serverName}' to be created");

            await Assert.That(svc).IsNotNull();
            await Assert.That(svc.Metadata.Name).IsEqualTo(serverName);
            await Assert.That(svc.Spec.Type).IsEqualTo("ClusterIP");
            await Assert.That(svc.Spec.Ports).Count().IsEqualTo(1);
            await Assert.That(svc.Spec.Ports[0].Port).IsEqualTo(25565);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidServer_CreatesConfigMap(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "happy-cm";
            await CreateValidMinecraftServer(K3s.Client, ns, serverName);

            // Wait for the ConfigMap to be created by the operator
            var cm = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.CoreV1.ReadNamespacedConfigMapAsync(serverName, ns),
                timeout: TimeSpan.FromSeconds(60),
                description: $"ConfigMap '{serverName}' to be created");

            await Assert.That(cm).IsNotNull();
            await Assert.That(cm.Metadata.Name).IsEqualTo(serverName);
            await Assert.That(cm.Data).IsNotNull();
            await Assert.That(cm.Data.ContainsKey("server-type")).IsTrue();
            await Assert.That(cm.Data["server-type"]).IsEqualTo("Vanilla");
            await Assert.That(cm.Data.ContainsKey("server.properties")).IsTrue();
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidServer_HasVolumeClaimTemplate(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "happy-pvc";
            await CreateValidMinecraftServer(K3s.Client, ns, serverName);

            // Wait for the StatefulSet to be created
            var sts = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(serverName, ns),
                timeout: TimeSpan.FromSeconds(60),
                description: $"StatefulSet '{serverName}' with VolumeClaimTemplate");

            // Verify VolumeClaimTemplate exists (storage is enabled in our test manifest)
            await Assert.That(sts.Spec.VolumeClaimTemplates).Count().IsEqualTo(1);

            var vct = sts.Spec.VolumeClaimTemplates[0];
            await Assert.That(vct.Metadata.Name).IsEqualTo("data");
            await Assert.That(vct.Spec.Resources.Requests["storage"].ToString()).IsEqualTo("1Gi");
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidServer_StatusIsUpdated(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "happy-status";
            await CreateValidMinecraftServer(K3s.Client, ns, serverName);

            // Wait for the status to be updated by the operator
            // The operator sets phase to Provisioning initially, then updates based on replica state
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var server = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
                    if (server is null) return false;

                    var json = ((JsonElement)server).GetRawText();
                    // Check that status exists and has been set (phase is no longer empty/Pending)
                    return json.Contains("\"phase\"") &&
                           (json.Contains("Provisioning") || json.Contains("Running") || json.Contains("Paused"));
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "MinecraftServer status to be updated");

            // Get the final status
            var result = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
            await Assert.That(result).IsNotNull();

            var resultJson = ((JsonElement)result!).GetRawText();
            // The status should contain conditions set by the operator
            await Assert.That(resultJson).Contains("conditions");
            await Assert.That(resultJson).Contains("currentImage");
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    /// <summary>
    /// Creates a valid MinecraftServer CR using the custom objects API.
    /// </summary>
    private static async Task CreateValidMinecraftServer(
        IKubernetes client,
        string namespaceName,
        string name)
    {
        var server = new Dictionary<string, object>
        {
            ["apiVersion"] = "minecraft.hengeveld.dev/v1alpha1",
            ["kind"] = "MinecraftServer",
            ["metadata"] = new Dictionary<string, object>
            {
                ["name"] = name,
                ["namespace"] = namespaceName,
            },
            ["spec"] = new Dictionary<string, object>
            {
                ["acceptEula"] = true,
                ["replicas"] = 1,
                ["server"] = new Dictionary<string, object>
                {
                    ["type"] = "Vanilla",
                    ["version"] = "1.20.4",
                },
                ["properties"] = new Dictionary<string, object>
                {
                    ["difficulty"] = "Normal",
                    ["gameMode"] = "Survival",
                    ["maxPlayers"] = 10,
                    ["onlineMode"] = false,
                    ["viewDistance"] = 10,
                    ["simulationDistance"] = 10,
                    ["levelName"] = "world",
                    ["serverPort"] = 25565,
                },
                ["jvm"] = new Dictionary<string, object>
                {
                    ["initialMemory"] = "512m",
                    ["maxMemory"] = "1G",
                },
                ["resources"] = new Dictionary<string, object>
                {
                    ["cpuRequest"] = "250m",
                    ["memoryRequest"] = "512Mi",
                },
                ["storage"] = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["size"] = "1Gi",
                    ["mountPath"] = "/data",
                },
                ["service"] = new Dictionary<string, object>
                {
                    ["type"] = "ClusterIP",
                },
            },
        };

        await KubernetesHelper.CreateMinecraftServerAsync(client, namespaceName, server);
    }
}
