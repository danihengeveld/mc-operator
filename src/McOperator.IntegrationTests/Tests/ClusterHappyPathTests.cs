using System.Text.Json;
using k8s;
using McOperator.IntegrationTests.Infrastructure;

namespace McOperator.IntegrationTests.Tests;

/// <summary>
/// Tests that creating a valid MinecraftServerCluster CR triggers reconciliation
/// and results in the expected child resources being created.
/// </summary>
public class ClusterHappyPathTests
{
    [ClassDataSource<K3sFixture>(Shared = SharedType.PerAssembly)]
    public required K3sFixture K3s { get; init; }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidCluster_CreatesBackendMinecraftServers(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var clusterName = "cluster-backends";
            await CreateValidMinecraftServerCluster(K3s.Client, ns, clusterName, replicas: 2);

            // Wait for the backend MinecraftServer CRs to be created by the operator
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var servers = await K3s.Client.CustomObjects.ListNamespacedCustomObjectAsync(
                        group: "mc-operator.dhv.sh",
                        version: "v1alpha1",
                        namespaceParameter: ns,
                        plural: "minecraftservers", cancellationToken: cancellationToken);

                    var jsonElement = (JsonElement)servers;
                    if (!jsonElement.TryGetProperty("items", out var items)) return false;
                    return items.GetArrayLength() >= 2;
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "2 backend MinecraftServer CRs to be created");

            // Verify exactly 2 MinecraftServer CRs exist
            var serverList = await K3s.Client.CustomObjects.ListNamespacedCustomObjectAsync(
                group: "mc-operator.dhv.sh",
                version: "v1alpha1",
                namespaceParameter: ns,
                plural: "minecraftservers", cancellationToken: cancellationToken);

            var serverListJson = (JsonElement)serverList;
            var serverItems = serverListJson.GetProperty("items");
            await Assert.That(serverItems.GetArrayLength()).IsEqualTo(2);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidCluster_CreatesProxyDeployment(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var clusterName = "cluster-proxy-dep";
            await CreateValidMinecraftServerCluster(K3s.Client, ns, clusterName);

            var proxyDeploymentName = $"{clusterName}-proxy";

            // Wait for the proxy Deployment to be created by the operator
            var deployment = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.AppsV1.ReadNamespacedDeploymentAsync(proxyDeploymentName, ns, cancellationToken: cancellationToken),
                timeout: TimeSpan.FromSeconds(60),
                description: $"Deployment '{proxyDeploymentName}' to be created");

            await Assert.That(deployment).IsNotNull();
            await Assert.That(deployment.Metadata.Name).IsEqualTo(proxyDeploymentName);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidCluster_CreatesProxyService(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var clusterName = "cluster-proxy-svc";
            await CreateValidMinecraftServerCluster(K3s.Client, ns, clusterName);

            var proxyServiceName = $"{clusterName}-proxy";

            // Wait for the proxy Service to be created by the operator
            var svc = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.CoreV1.ReadNamespacedServiceAsync(proxyServiceName, ns, cancellationToken: cancellationToken),
                timeout: TimeSpan.FromSeconds(60),
                description: $"Service '{proxyServiceName}' to be created");

            await Assert.That(svc).IsNotNull();
            await Assert.That(svc.Metadata.Name).IsEqualTo(proxyServiceName);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidCluster_CreatesProxyConfigMap(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var clusterName = "cluster-proxy-cm";
            await CreateValidMinecraftServerCluster(K3s.Client, ns, clusterName);

            var proxyConfigMapName = $"{clusterName}-proxy-config";

            // Wait for the proxy ConfigMap to be created by the operator
            var cm = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.CoreV1.ReadNamespacedConfigMapAsync(proxyConfigMapName, ns, cancellationToken: cancellationToken),
                timeout: TimeSpan.FromSeconds(60),
                description: $"ConfigMap '{proxyConfigMapName}' to be created");

            await Assert.That(cm).IsNotNull();
            await Assert.That(cm.Metadata.Name).IsEqualTo(proxyConfigMapName);
            await Assert.That(cm.Data).IsNotNull();
            await Assert.That(cm.Data.ContainsKey("velocity.toml")).IsTrue();
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task CreateValidCluster_StatusIsUpdated(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var clusterName = "cluster-status";
            await CreateValidMinecraftServerCluster(K3s.Client, ns, clusterName, replicas: 2);

            // Wait for the cluster status to be updated by the operator
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var cluster = await KubernetesHelper.GetMinecraftServerClusterAsync(K3s.Client, ns, clusterName);
                    if (cluster is null) return false;

                    var jsonElement = (JsonElement)cluster;
                    if (!jsonElement.TryGetProperty("status", out var status)) return false;
                    if (!status.TryGetProperty("phase", out var phase)) return false;
                    return phase.ValueKind == JsonValueKind.String && phase.GetString() is not null;
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "MinecraftServerCluster status to have phase set");

            // Get the final status
            var result = await KubernetesHelper.GetMinecraftServerClusterAsync(K3s.Client, ns, clusterName);
            await Assert.That(result).IsNotNull();

            var jsonResult = (JsonElement)result!;
            var statusObj = jsonResult.GetProperty("status");

            await Assert.That(statusObj.TryGetProperty("phase", out _)).IsTrue();
            await Assert.That(statusObj.TryGetProperty("totalServers", out _)).IsTrue();
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    /// <summary>
    /// Creates a valid MinecraftServerCluster CR using the custom objects API.
    /// </summary>
    private static async Task CreateValidMinecraftServerCluster(
        IKubernetes client,
        string namespaceName,
        string name,
        int replicas = 2)
    {
        var cluster = new Dictionary<string, object>
        {
            ["apiVersion"] = "mc-operator.dhv.sh/v1alpha1",
            ["kind"] = "MinecraftServerCluster",
            ["metadata"] = new Dictionary<string, object> { ["name"] = name, ["namespace"] = namespaceName, },
            ["spec"] = new Dictionary<string, object>
            {
                ["template"] = new Dictionary<string, object>
                {
                    ["acceptEula"] = true,
                    ["server"] = new Dictionary<string, object> { ["type"] = "Paper", ["version"] = "1.20.4", },
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
                        new Dictionary<string, object>
                        {
                            ["enabled"] = true, ["size"] = "1Gi", ["mountPath"] = "/data",
                        },
                },
                ["scaling"] = new Dictionary<string, object> { ["mode"] = "Static", ["replicas"] = replicas, },
                ["proxy"] = new Dictionary<string, object>
                {
                    ["proxyPort"] = 25577,
                    ["maxPlayers"] = 20,
                    ["onlineMode"] = false,
                    ["playerForwardingMode"] = "Modern",
                    ["service"] = new Dictionary<string, object> { ["type"] = "ClusterIP", },
                },
            },
        };

        await KubernetesHelper.CreateMinecraftServerClusterAsync(client, namespaceName, cluster);
    }
}
