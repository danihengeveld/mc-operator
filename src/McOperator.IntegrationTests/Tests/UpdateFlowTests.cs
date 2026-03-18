using System.Text.Json;
using k8s;
using McOperator.Builders;
using McOperator.IntegrationTests.Infrastructure;
using TUnit.Assertions.Extensions;

namespace McOperator.IntegrationTests.Tests;

/// <summary>
/// Tests that updating a MinecraftServer CR spec triggers reconciliation
/// and updates the underlying Kubernetes resources.
/// </summary>
public class UpdateFlowTests
{
    [ClassDataSource<K3sFixture>(Shared = SharedType.PerAssembly)]
    public required K3sFixture K3s { get; init; }

    [Test]
    [Timeout(120_000)]
    public async Task UpdateServerType_UpdatesStatefulSetEnvVars(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "update-type";
            await CreateMinecraftServer(K3s.Client, ns, serverName, serverType: "Vanilla", version: "1.20.4");

            // Wait for initial StatefulSet creation
            var sts = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(serverName, ns),
                timeout: TimeSpan.FromSeconds(60),
                description: $"StatefulSet '{serverName}' to be created");

            // Verify initial TYPE env var
            var container = sts.Spec.Template.Spec.Containers[0];
            var typeEnv = container.Env.FirstOrDefault(e => e.Name == "TYPE");
            await Assert.That(typeEnv).IsNotNull();
            await Assert.That(typeEnv!.Value).IsEqualTo("VANILLA");

            // Update the server type from Vanilla to Paper
            await KubernetesHelper.PatchMinecraftServerAsync(
                K3s.Client, ns, serverName,
                new Dictionary<string, object>
                {
                    ["spec"] = new Dictionary<string, object>
                    {
                        ["server"] = new Dictionary<string, object>
                        {
                            ["type"] = "Paper",
                        },
                    },
                });

            // Wait for the StatefulSet to be updated with the new server type
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var updated = await K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(serverName, ns);
                    var env = updated.Spec.Template.Spec.Containers[0].Env
                        .FirstOrDefault(e => e.Name == "TYPE");
                    return env?.Value == "PAPER";
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "StatefulSet TYPE env var to be updated to PAPER");

            // Verify the update happened
            var updatedSts = await K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(serverName, ns);
            var updatedTypeEnv = updatedSts.Spec.Template.Spec.Containers[0].Env
                .FirstOrDefault(e => e.Name == "TYPE");
            await Assert.That(updatedTypeEnv!.Value).IsEqualTo("PAPER");
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task UpdateMaxPlayers_UpdatesConfigMap(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "update-players";
            var configMapName = ConfigMapBuilder.ConfigMapName(serverName);
            await CreateMinecraftServer(K3s.Client, ns, serverName);

            // Wait for initial ConfigMap creation
            // The operator names ConfigMaps as "{serverName}-config"
            var cm = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.CoreV1.ReadNamespacedConfigMapAsync(configMapName, ns),
                timeout: TimeSpan.FromSeconds(60),
                description: $"ConfigMap '{configMapName}' to be created");

            await Assert.That(cm.Data["server.properties"]).Contains("max-players=10");

            // Update max players
            await KubernetesHelper.PatchMinecraftServerAsync(
                K3s.Client, ns, serverName,
                new Dictionary<string, object>
                {
                    ["spec"] = new Dictionary<string, object>
                    {
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["maxPlayers"] = 50,
                        },
                    },
                });

            // Wait for the ConfigMap to be updated
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var updated = await K3s.Client.CoreV1.ReadNamespacedConfigMapAsync(configMapName, ns);
                    return updated.Data["server.properties"].Contains("max-players=50");
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "ConfigMap to reflect maxPlayers=50");

            var updatedCm = await K3s.Client.CoreV1.ReadNamespacedConfigMapAsync(configMapName, ns);
            await Assert.That(updatedCm.Data["server.properties"]).Contains("max-players=50");
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task SetReplicasToZero_PausesServer(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "update-pause";
            await CreateMinecraftServer(K3s.Client, ns, serverName);

            // Wait for initial StatefulSet creation with replicas=1
            var sts = await KubernetesHelper.WaitForResourceAsync(
                () => K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(serverName, ns),
                timeout: TimeSpan.FromSeconds(60),
                description: $"StatefulSet '{serverName}' with replicas=1");

            await Assert.That(sts.Spec.Replicas).IsEqualTo(1);

            // Pause the server by setting replicas to 0
            await KubernetesHelper.PatchMinecraftServerAsync(
                K3s.Client, ns, serverName,
                new Dictionary<string, object>
                {
                    ["spec"] = new Dictionary<string, object>
                    {
                        ["replicas"] = 0,
                    },
                });

            // Wait for the StatefulSet to scale to 0
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var updated = await K3s.Client.AppsV1.ReadNamespacedStatefulSetAsync(serverName, ns);
                    return updated.Spec.Replicas == 0;
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "StatefulSet replicas to be 0");

            // Verify the status shows Paused
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var server = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
                    if (server is null) return false;
                    var json = ((JsonElement)server).GetRawText();
                    return json.Contains("\"Paused\"");
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "MinecraftServer phase to be Paused");
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    private static async Task CreateMinecraftServer(
        IKubernetes client,
        string namespaceName,
        string name,
        string serverType = "Vanilla",
        string version = "1.20.4")
    {
        var server = new Dictionary<string, object>
        {
            ["apiVersion"] = "mc-operator.dhv.sh/v1alpha1",
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
                    ["type"] = serverType,
                    ["version"] = version,
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
