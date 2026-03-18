using System.Text.Json;
using k8s;
using McOperator.IntegrationTests.Infrastructure;
using TUnit.Assertions.Extensions;

namespace McOperator.IntegrationTests.Tests;

/// <summary>
/// Tests that the operator correctly updates status conditions on MinecraftServer resources.
/// Verifies that status reflects the actual state of child resources.
/// </summary>
public class StatusConditionTests
{
    [ClassDataSource<K3sFixture>(Shared = SharedType.PerAssembly)]
    public required K3sFixture K3s { get; init; }

    [Test]
    [Timeout(120_000)]
    public async Task RunningServer_HasProvisioningOrRunningPhase(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "status-phase";
            await CreateValidMinecraftServer(K3s.Client, ns, serverName);

            // Wait for the status to contain a recognized phase
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var server = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
                    if (server is null) return false;
                    var json = ((JsonElement)server).GetRawText();
                    return json.Contains("\"Provisioning\"") || json.Contains("\"Running\"");
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "MinecraftServer to have Provisioning or Running phase");

            var result = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
            var jsonElement = (JsonElement)result!;
            var phase = jsonElement.GetProperty("status").GetProperty("phase").GetString();

            // Phase should be Provisioning (pods starting) or Running (if pod came up fast)
            await Assert.That(new[] { "Provisioning", "Running" }).Contains(phase!);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task RunningServer_HasConditions(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "status-cond";
            await CreateValidMinecraftServer(K3s.Client, ns, serverName);

            // Wait for conditions to be set
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var server = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
                    if (server is null) return false;
                    var jsonElement = (JsonElement)server;
                    if (!jsonElement.TryGetProperty("status", out var status)) return false;
                    if (!status.TryGetProperty("conditions", out var conditions)) return false;
                    return conditions.GetArrayLength() > 0;
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "MinecraftServer to have conditions");

            var result = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
            var jsonElement = (JsonElement)result!;
            var conditions = jsonElement.GetProperty("status").GetProperty("conditions");

            // The operator sets Available, Progressing, and Degraded conditions
            var conditionTypes = new List<string>();
            foreach (var condition in conditions.EnumerateArray())
            {
                conditionTypes.Add(condition.GetProperty("type").GetString()!);
            }

            await Assert.That(conditionTypes).Contains("Available");
            await Assert.That(conditionTypes).Contains("Progressing");
            await Assert.That(conditionTypes).Contains("Degraded");
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task RunningServer_HasCurrentImageInStatus(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "status-image";
            await CreateValidMinecraftServer(K3s.Client, ns, serverName);

            // Wait for currentImage to be set in status
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var server = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
                    if (server is null) return false;
                    var jsonElement = (JsonElement)server;
                    if (!jsonElement.TryGetProperty("status", out var status)) return false;
                    return status.TryGetProperty("currentImage", out var img) &&
                           img.GetString() is not null;
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "MinecraftServer status to have currentImage");

            var result = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
            var jsonElement = (JsonElement)result!;
            var currentImage = jsonElement.GetProperty("status").GetProperty("currentImage").GetString();
            var currentVersion = jsonElement.GetProperty("status").GetProperty("currentVersion").GetString();

            await Assert.That(currentImage).IsEqualTo("itzg/minecraft-server:latest");
            await Assert.That(currentVersion).IsEqualTo("1.20.4");
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task PausedServer_HasPausedPhaseAndAvailableFalse(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var serverName = "status-paused";

            // Create a server with replicas=0 (paused from the start)
            var server = new Dictionary<string, object>
            {
                ["apiVersion"] = "mc-operator.dhv.sh/v1alpha1",
                ["kind"] = "MinecraftServer",
                ["metadata"] = new Dictionary<string, object>
                {
                    ["name"] = serverName,
                    ["namespace"] = ns,
                },
                ["spec"] = new Dictionary<string, object>
                {
                    ["acceptEula"] = true,
                    ["replicas"] = 0,
                    ["server"] = new Dictionary<string, object>
                    {
                        ["type"] = "Vanilla",
                        ["version"] = "1.20.4",
                    },
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["maxPlayers"] = 10,
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

            await KubernetesHelper.CreateMinecraftServerAsync(K3s.Client, ns, server);

            // Wait for the status to show Paused
            await KubernetesHelper.WaitForConditionAsync(
                async () =>
                {
                    var result = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
                    if (result is null) return false;
                    var json = ((JsonElement)result).GetRawText();
                    return json.Contains("\"Paused\"");
                },
                timeout: TimeSpan.FromSeconds(60),
                description: "MinecraftServer phase to be Paused");

            var finalResult = await KubernetesHelper.GetMinecraftServerAsync(K3s.Client, ns, serverName);
            var jsonElement = (JsonElement)finalResult!;
            var phase = jsonElement.GetProperty("status").GetProperty("phase").GetString();
            await Assert.That(phase).IsEqualTo("Paused");

            // Available condition should be False when paused
            var conditions = jsonElement.GetProperty("status").GetProperty("conditions");
            var availableCondition = conditions.EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("type").GetString() == "Available");
            await Assert.That(availableCondition.GetProperty("status").GetString()).IsEqualTo("False");
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    private static async Task CreateValidMinecraftServer(
        IKubernetes client,
        string namespaceName,
        string name)
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
