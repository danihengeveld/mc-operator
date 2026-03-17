using k8s;
using k8s.Models;
using KubeOps.Operator;
using McOperator.Controllers;
using McOperator.Entities;
using McOperator.Finalizers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.K3s;
using TUnit.Core.Interfaces;

namespace McOperator.IntegrationTests.Infrastructure;

/// <summary>
/// Assembly-wide fixture that manages a shared k3s cluster and in-process operator.
///
/// Architecture decision: The operator runs in-process connected to the k3s cluster via kubeconfig.
/// This approach is:
/// - Fastest: No Docker image build or registry push needed
/// - Most reliable: No Docker-in-Docker complexity
/// - Closest to real usage: Still tests against a real Kubernetes API server
/// - Easy to debug: Operator logs are directly available in test output
///
/// The tradeoff is that webhook admission testing requires the operator to be network-reachable
/// from the k3s API server, which isn't the case in-process. Webhook logic is already
/// well-covered by unit tests (ValidationWebhookTests), so this is an acceptable tradeoff.
/// </summary>
public class K3sFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly K3sContainer _container;
    private WebApplication? _operatorApp;
    private string? _kubeconfigPath;

    /// <summary>
    /// Kubernetes client for test assertions and resource management.
    /// </summary>
    public IKubernetes Client { get; private set; } = null!;

    public K3sFixture()
    {
        _container = new K3sBuilder("rancher/k3s:v1.32.3-k3s1").Build();
    }

    public async Task InitializeAsync()
    {
        // 1. Start k3s cluster (K3sBuilder has built-in readiness waiting)
        await _container.StartAsync();

        // 2. Write kubeconfig to a temp file and create client
        var kubeconfigYaml = await _container.GetKubeconfigAsync();
        _kubeconfigPath = Path.Combine(Path.GetTempPath(), $"mc-operator-test-{Guid.NewGuid()}.kubeconfig");
        await File.WriteAllTextAsync(_kubeconfigPath, kubeconfigYaml);

        var clientConfig = KubernetesClientConfiguration.BuildConfigFromConfigFile(_kubeconfigPath);
        Client = new Kubernetes(clientConfig);

        // 3. Apply CRDs
        await ApplyCrd();

        // 4. Wait for CRD to be established
        await WaitForCrdEstablished();

        // 5. Start operator in-process
        await StartOperator();
    }

    public async ValueTask DisposeAsync()
    {
        if (_operatorApp is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _operatorApp.StopAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout during shutdown is acceptable
            }

            await _operatorApp.DisposeAsync();
        }

        Client?.Dispose();
        await _container.DisposeAsync();

        if (_kubeconfigPath is not null && File.Exists(_kubeconfigPath))
        {
            File.Delete(_kubeconfigPath);
        }
    }

    private async Task ApplyCrd()
    {
        var crdPath = FindCrdPath();
        var crdYaml = await File.ReadAllTextAsync(crdPath);
        var crd = KubernetesYaml.Deserialize<V1CustomResourceDefinition>(crdYaml);

        try
        {
            await Client.ApiextensionsV1.CreateCustomResourceDefinitionAsync(crd);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // CRD already exists, update it
            await Client.ApiextensionsV1.ReplaceCustomResourceDefinitionAsync(crd, crd.Metadata.Name);
        }
    }

    private async Task WaitForCrdEstablished()
    {
        await KubernetesHelper.WaitForConditionAsync(
            async () =>
            {
                var crd = await Client.ApiextensionsV1.ReadCustomResourceDefinitionAsync(
                    "minecraftservers.minecraft.hengeveld.dev");
                return crd.Status?.Conditions?.Any(c =>
                    c.Type == "Established" && c.Status == "True") == true;
            },
            timeout: TimeSpan.FromSeconds(30),
            description: "CRD to be established");
    }

    private async Task StartOperator()
    {
        // Build the operator host with the same configuration as Program.cs
        // but configured to use the k3s kubeconfig
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "IntegrationTest",
        });

        // Point the Kubernetes client at our k3s cluster
        Environment.SetEnvironmentVariable("KUBECONFIG", _kubeconfigPath);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services
            .AddKubernetesOperator(settings =>
            {
                settings.Name = "mc-operator";
                // Disable leader election for testing — single instance
                settings.LeaderElectionType = KubeOps.Abstractions.Builder.LeaderElectionType.Single;
            })
            .AddController<MinecraftServerController, MinecraftServer>()
            .AddFinalizer<MinecraftServerFinalizer, MinecraftServer>(
                "mc-operator.minecraft.hengeveld.dev/finalizer");

        builder.Services.AddControllers();

        // Use random port to avoid conflicts
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _operatorApp = builder.Build();
        _operatorApp.UseRouting();
        _operatorApp.MapControllers();

        // Start the operator in the background
        await _operatorApp.StartAsync();
    }

    /// <summary>
    /// Finds the CRD YAML file by walking up from the build output directory
    /// until we find the solution root (identified by McOperator.slnx).
    /// </summary>
    private static string FindCrdPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var crdFile = Path.Combine(directory.FullName, "manifests", "crd", "minecraftservers.yaml");
            if (File.Exists(crdFile))
            {
                return crdFile;
            }

            // Check for solution file as a safety stop
            if (File.Exists(Path.Combine(directory.FullName, "McOperator.slnx")))
            {
                throw new FileNotFoundException(
                    $"Found solution root at {directory.FullName} but CRD file is missing at manifests/crd/minecraftservers.yaml");
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find CRD file. Searched upward from {AppContext.BaseDirectory} for manifests/crd/minecraftservers.yaml");
    }
}
