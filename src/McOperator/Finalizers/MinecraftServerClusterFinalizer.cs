using k8s.Models;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.KubernetesClient;
using McOperator.Entities;
using McOperator.Extensions;

namespace McOperator.Finalizers;

/// <summary>
/// Finalizer for MinecraftServerCluster resources.
/// Ensures orderly cleanup of backend MinecraftServer instances.
/// The proxy Deployment, Service, and ConfigMap are cleaned up via OwnerReference
/// garbage collection. Backend MinecraftServer instances also have OwnerReferences
/// but we log cleanup status for visibility.
/// </summary>
public class MinecraftServerClusterFinalizer : IEntityFinalizer<MinecraftServerCluster>
{
    private readonly IKubernetesClient _client;
    private readonly ILogger<MinecraftServerClusterFinalizer> _logger;

    public MinecraftServerClusterFinalizer(
        IKubernetesClient client,
        ILogger<MinecraftServerClusterFinalizer> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Called when a MinecraftServerCluster is being deleted and this finalizer is attached.
    /// </summary>
    public async Task<ReconciliationResult<MinecraftServerCluster>> FinalizeAsync(
        MinecraftServerCluster cluster,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Finalizing MinecraftServerCluster {Namespace}/{Name}",
            cluster.Namespace(), cluster.Name());

        // List all backend servers owned by this cluster to log cleanup
        var allServers = await _client.ListAsync<MinecraftServer>(
            cluster.Namespace(), cancellationToken: cancellationToken);
        var ownedServers = allServers
            .Where(s => s.Metadata.OwnerReferences?.Any(o =>
                o.Kind == OperatorConstants.ClusterKind && o.Name == cluster.Name()) == true)
            .ToList();

        if (ownedServers.Count > 0)
        {
            _logger.LogInformation(
                "MinecraftServerCluster {Namespace}/{Name}: {Count} backend servers will be garbage-collected.",
                cluster.Namespace(), cluster.Name(), ownedServers.Count);

            foreach (var server in ownedServers)
            {
                _logger.LogInformation(
                    "Backend server {Name} in {Namespace} will be garbage-collected.",
                    server.Name(), server.Namespace());
            }
        }

        return ReconciliationResult<MinecraftServerCluster>.Success(cluster);
    }
}
