using k8s;
using k8s.Autorest;
using McOperator.IntegrationTests.Infrastructure;
using TUnit.Assertions.Extensions;

namespace McOperator.IntegrationTests.Tests;

/// <summary>
/// Tests that invalid MinecraftServer CRs are rejected by the CRD schema validation.
///
/// Note: Custom webhook validation (e.g. EULA, JVM memory checks) is not tested here
/// because webhooks require the operator to be network-reachable from the API server.
/// Those are thoroughly covered by unit tests in ValidationWebhookTests.
///
/// These tests focus on CRD OpenAPI schema validation which happens at the API server level.
/// </summary>
public class ValidationTests
{
    [ClassDataSource<K3sFixture>(Shared = SharedType.PerAssembly)]
    public required K3sFixture K3s { get; init; }

    [Test]
    [Timeout(120_000)]
    public async Task InvalidServerType_IsRejectedByCrdSchema(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            // "Forge" is not in the CRD enum (only Vanilla, Paper, Spigot, Bukkit)
            var server = new Dictionary<string, object>
            {
                ["apiVersion"] = "mc-operator.dhv.sh/v1alpha1",
                ["kind"] = "MinecraftServer",
                ["metadata"] = new Dictionary<string, object>
                {
                    ["name"] = "invalid-type",
                    ["namespace"] = ns,
                },
                ["spec"] = new Dictionary<string, object>
                {
                    ["acceptEula"] = true,
                    ["server"] = new Dictionary<string, object>
                    {
                        ["type"] = "Forge",
                        ["version"] = "1.20.4",
                    },
                },
            };

            var ex = await Assert.ThrowsAsync<HttpOperationException>(
                () => KubernetesHelper.CreateMinecraftServerAsync(K3s.Client, ns, server));

            // The API server should reject this with a 422 Unprocessable Entity
            await Assert.That((int)ex!.Response!.StatusCode).IsEqualTo(422);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task MissingRequiredServerField_IsRejectedByCrdSchema(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            // Missing required 'server' field
            var server = new Dictionary<string, object>
            {
                ["apiVersion"] = "mc-operator.dhv.sh/v1alpha1",
                ["kind"] = "MinecraftServer",
                ["metadata"] = new Dictionary<string, object>
                {
                    ["name"] = "missing-server",
                    ["namespace"] = ns,
                },
                ["spec"] = new Dictionary<string, object>
                {
                    ["acceptEula"] = true,
                },
            };

            var ex = await Assert.ThrowsAsync<HttpOperationException>(
                () => KubernetesHelper.CreateMinecraftServerAsync(K3s.Client, ns, server));

            await Assert.That((int)ex!.Response!.StatusCode).IsEqualTo(422);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task InvalidServiceType_IsRejectedByCrdSchema(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            // "ExternalName" is not in the CRD enum (only ClusterIP, NodePort, LoadBalancer)
            var server = new Dictionary<string, object>
            {
                ["apiVersion"] = "mc-operator.dhv.sh/v1alpha1",
                ["kind"] = "MinecraftServer",
                ["metadata"] = new Dictionary<string, object>
                {
                    ["name"] = "invalid-svc-type",
                    ["namespace"] = ns,
                },
                ["spec"] = new Dictionary<string, object>
                {
                    ["acceptEula"] = true,
                    ["server"] = new Dictionary<string, object>
                    {
                        ["type"] = "Vanilla",
                        ["version"] = "1.20.4",
                    },
                    ["service"] = new Dictionary<string, object>
                    {
                        ["type"] = "ExternalName",
                    },
                },
            };

            var ex = await Assert.ThrowsAsync<HttpOperationException>(
                () => KubernetesHelper.CreateMinecraftServerAsync(K3s.Client, ns, server));

            await Assert.That((int)ex!.Response!.StatusCode).IsEqualTo(422);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task InvalidImagePullPolicy_IsRejectedByCrdSchema(CancellationToken cancellationToken)
    {
        var ns = await KubernetesHelper.CreateTestNamespaceAsync(K3s.Client);
        try
        {
            var server = new Dictionary<string, object>
            {
                ["apiVersion"] = "mc-operator.dhv.sh/v1alpha1",
                ["kind"] = "MinecraftServer",
                ["metadata"] = new Dictionary<string, object>
                {
                    ["name"] = "invalid-pull",
                    ["namespace"] = ns,
                },
                ["spec"] = new Dictionary<string, object>
                {
                    ["acceptEula"] = true,
                    ["server"] = new Dictionary<string, object>
                    {
                        ["type"] = "Vanilla",
                        ["version"] = "1.20.4",
                    },
                    ["imagePullPolicy"] = "InvalidPolicy",
                },
            };

            var ex = await Assert.ThrowsAsync<HttpOperationException>(
                () => KubernetesHelper.CreateMinecraftServerAsync(K3s.Client, ns, server));

            await Assert.That((int)ex!.Response!.StatusCode).IsEqualTo(422);
        }
        finally
        {
            await KubernetesHelper.DeleteNamespaceAsync(K3s.Client, ns);
        }
    }
}
