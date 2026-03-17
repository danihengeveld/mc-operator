using k8s.Models;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using KubeOps.KubernetesClient;
using McOperator.Entities;

namespace McOperator.Finalizers;

/// <summary>
/// Finalizer for MinecraftServer resources.
/// Handles cleanup of PVCs when DeleteWithServer is set to true.
/// When DeleteWithServer is false (the default), the PVC is retained
/// to prevent accidental data loss.
/// </summary>
public class MinecraftServerFinalizer : IEntityFinalizer<MinecraftServer>
{
    private readonly IKubernetesClient _client;
    private readonly ILogger<MinecraftServerFinalizer> _logger;

    public MinecraftServerFinalizer(
        IKubernetesClient client,
        ILogger<MinecraftServerFinalizer> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Called when a MinecraftServer is being deleted and this finalizer is attached.
    /// </summary>
    public async Task<ReconciliationResult<MinecraftServer>> FinalizeAsync(MinecraftServer server, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Finalizing MinecraftServer {Namespace}/{Name}",
            server.Namespace(), server.Name());

        if (server.Spec.Storage.Enabled && server.Spec.Storage.DeleteWithServer)
        {
            await DeletePvc(server, cancellationToken);
        }
        else if (server.Spec.Storage.Enabled)
        {
            _logger.LogInformation(
                "MinecraftServer {Namespace}/{Name}: PVC retained (DeleteWithServer=false). " +
                "PVC 'data-{Name}-0' in namespace '{Namespace}' must be manually deleted if no longer needed.",
                server.Namespace(), server.Name(), server.Name(), server.Namespace());
        }

        return ReconciliationResult<MinecraftServer>.Success(server);
    }

    private async Task DeletePvc(MinecraftServer server, CancellationToken cancellationToken)
    {
        // StatefulSet PVC naming convention: <volumeName>-<podName>-<ordinal>
        var pvcName = $"data-{server.Name()}-0";

        try
        {
            var pvc = await _client.GetAsync<V1PersistentVolumeClaim>(
                pvcName, server.Namespace(), cancellationToken);

            if (pvc is null)
            {
                _logger.LogInformation(
                    "PVC {PvcName} not found in {Namespace}, nothing to delete",
                    pvcName, server.Namespace());
                return;
            }

            _logger.LogInformation(
                "Deleting PVC {PvcName} in {Namespace} (DeleteWithServer=true)",
                pvcName, server.Namespace());

            await _client.DeleteAsync(pvc, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete PVC {PvcName} for MinecraftServer {Namespace}/{Name}",
                pvcName, server.Namespace(), server.Name());
            throw;
        }
    }
}
