using k8s;
using k8s.Models;

namespace McOperator.IntegrationTests.Infrastructure;

/// <summary>
/// Helper utilities for Kubernetes operations in integration tests.
/// Provides methods for creating namespaces, applying resources,
/// polling for conditions, and querying cluster state.
/// </summary>
public static class KubernetesHelper
{
    /// <summary>
    /// Creates a unique namespace for test isolation and returns its name.
    /// </summary>
    public static async Task<string> CreateTestNamespaceAsync(IKubernetes client, string prefix = "test")
    {
        var name = $"{prefix}-{Guid.NewGuid():N}"[..32];
        var ns = new V1Namespace
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Labels = new Dictionary<string, string>
                {
                    ["mc-operator-test"] = "true",
                },
            },
        };

        await client.CoreV1.CreateNamespaceAsync(ns);
        return name;
    }

    /// <summary>
    /// Deletes a namespace and all resources in it.
    /// </summary>
    public static async Task DeleteNamespaceAsync(IKubernetes client, string namespaceName)
    {
        try
        {
            await client.CoreV1.DeleteNamespaceAsync(namespaceName);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already deleted
        }
    }

    /// <summary>
    /// Polls a condition function until it returns true or the timeout expires.
    /// Uses exponential backoff starting from 500ms, capped at 5s.
    /// </summary>
    public static async Task WaitForConditionAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        string description,
        TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition())
                {
                    return;
                }
            }
            catch
            {
                // Swallow exceptions during polling; the condition will eventually time out
            }

            await Task.Delay(interval);
            // Exponential backoff, capped at 5 seconds
            interval = TimeSpan.FromMilliseconds(Math.Min(interval.TotalMilliseconds * 1.5, 5000));
        }

        throw new TimeoutException($"Timed out waiting for: {description} (timeout: {timeout})");
    }

    /// <summary>
    /// Waits for a specific resource to exist in the cluster.
    /// </summary>
    public static async Task<T> WaitForResourceAsync<T>(
        Func<Task<T?>> getter,
        TimeSpan timeout,
        string description) where T : class
    {
        T? result = null;

        await WaitForConditionAsync(
            async () =>
            {
                try
                {
                    result = await getter();
                    return result is not null;
                }
                catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return false;
                }
            },
            timeout,
            description);

        return result ?? throw new InvalidOperationException($"Resource was null after waiting: {description}");
    }

    /// <summary>
    /// Creates a MinecraftServer custom resource in the specified namespace.
    /// </summary>
    public static async Task<object> CreateMinecraftServerAsync(
        IKubernetes client,
        string namespaceName,
        object serverSpec)
    {
        return await client.CustomObjects.CreateNamespacedCustomObjectAsync(
            body: serverSpec,
            group: "mc-operator.dhv.sh",
            version: "v1alpha1",
            namespaceParameter: namespaceName,
            plural: "minecraftservers");
    }

    /// <summary>
    /// Gets a MinecraftServer custom resource.
    /// </summary>
    public static async Task<object?> GetMinecraftServerAsync(
        IKubernetes client,
        string namespaceName,
        string name)
    {
        try
        {
            return await client.CustomObjects.GetNamespacedCustomObjectAsync(
                group: "mc-operator.dhv.sh",
                version: "v1alpha1",
                namespaceParameter: namespaceName,
                plural: "minecraftservers",
                name: name);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Patches a MinecraftServer custom resource using a merge patch.
    /// </summary>
    public static async Task<object> PatchMinecraftServerAsync(
        IKubernetes client,
        string namespaceName,
        string name,
        object patchBody)
    {
        var patch = new V1Patch(patchBody, V1Patch.PatchType.MergePatch);
        return await client.CustomObjects.PatchNamespacedCustomObjectAsync(
            body: patch,
            group: "mc-operator.dhv.sh",
            version: "v1alpha1",
            namespaceParameter: namespaceName,
            plural: "minecraftservers",
            name: name);
    }

    /// <summary>
    /// Deletes a MinecraftServer custom resource.
    /// </summary>
    public static async Task DeleteMinecraftServerAsync(
        IKubernetes client,
        string namespaceName,
        string name)
    {
        try
        {
            await client.CustomObjects.DeleteNamespacedCustomObjectAsync(
                group: "mc-operator.dhv.sh",
                version: "v1alpha1",
                namespaceParameter: namespaceName,
                plural: "minecraftservers",
                name: name);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already deleted
        }
    }
}
