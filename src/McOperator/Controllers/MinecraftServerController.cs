using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using McOperator.Builders;
using McOperator.Entities;

namespace McOperator.Controllers;

/// <summary>
/// Reconciles MinecraftServer resources.
/// Manages the lifecycle of owned child resources:
/// - StatefulSet (server workload)
/// - Service (network exposure)
/// - ConfigMap (configuration/audit)
/// </summary>
[EntityRbac(typeof(MinecraftServer), Verbs = RbacVerb.All)]
[GenericRbac(Groups = ["apps"], Resources = ["statefulsets"], Verbs = RbacVerb.All)]
[GenericRbac(Groups = [""], Resources = ["services", "configmaps", "persistentvolumeclaims"], Verbs = RbacVerb.All)]
[GenericRbac(Groups = [""], Resources = ["events"], Verbs = RbacVerb.Create | RbacVerb.Patch)]
[GenericRbac(Groups = [""], Resources = ["pods"], Verbs = RbacVerb.Get)]
[GenericRbac(Groups = ["batch"], Resources = ["jobs"], Verbs = RbacVerb.All)]
[GenericRbac(Groups = ["coordination.k8s.io"], Resources = ["leases"], Verbs = RbacVerb.All)]
public class MinecraftServerController : IEntityController<MinecraftServer>
{
    private readonly IKubernetesClient _client;
    private readonly ILogger<MinecraftServerController> _logger;

    public MinecraftServerController(
        IKubernetesClient client,
        ILogger<MinecraftServerController> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Called when a MinecraftServer is created or updated.
    /// Reconciles all child resources to match the desired state.
    /// </summary>
    public async Task<ReconciliationResult<MinecraftServer>> ReconcileAsync(
        MinecraftServer server,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Reconciling MinecraftServer {Namespace}/{Name} (generation {Generation})",
            server.Namespace(), server.Name(), server.Generation());

        // Update status to Provisioning while we work
        await UpdatePhase(server, MinecraftServerPhase.Provisioning, "Reconciling resources", cancellationToken);

        try
        {
            // 1. Reconcile ConfigMap
            await ReconcileConfigMap(server, cancellationToken);

            // 2. Reconcile Service
            await ReconcileService(server, cancellationToken);

            // 3. Pre-pull the new image on the server's node before applying the
            //    StatefulSet update so that pod restart time is minimised.
            var desiredImage = StatefulSetBuilder.ResolveImage(server.Spec);
            if (!await EnsureImagePrePulled(server, desiredImage, cancellationToken))
            {
                await UpdatePhase(
                    server,
                    MinecraftServerPhase.Provisioning,
                    $"Pre-pulling image: {desiredImage}",
                    cancellationToken);
                return ReconciliationResult<MinecraftServer>.Success(server, requeueAfter: TimeSpan.FromSeconds(30));
            }

            // 4. Reconcile StatefulSet
            var sts = await ReconcileStatefulSet(server, cancellationToken);

            // 5. Update status based on StatefulSet state
            await UpdateStatus(server, sts, cancellationToken);

            return ReconciliationResult<MinecraftServer>.Success(server, requeueAfter: TimeSpan.FromMinutes(5));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile MinecraftServer {Namespace}/{Name}", server.Namespace(),
                server.Name());
            await UpdatePhase(server, MinecraftServerPhase.Failed, ex.Message, cancellationToken);
            return ReconciliationResult<MinecraftServer>.Failure(server, ex.Message, ex, TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    /// Called when a MinecraftServer is deleted.
    /// Child resources owned via OwnerReference are garbage-collected automatically.
    /// The finalizer handles PVC cleanup if DeleteWithServer is true.
    /// </summary>
    public Task<ReconciliationResult<MinecraftServer>> DeletedAsync(
        MinecraftServer server,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "MinecraftServer {Namespace}/{Name} deleted",
            server.Namespace(), server.Name());

        return Task.FromResult(ReconciliationResult<MinecraftServer>.Success(server));
    }

    private async Task ReconcileConfigMap(MinecraftServer server, CancellationToken cancellationToken)
    {
        var desired = ConfigMapBuilder.Build(server);
        var existing = await _client.GetAsync<V1ConfigMap>(desired.Name(), server.Namespace(), cancellationToken);

        if (existing is null)
        {
            _logger.LogInformation("Creating ConfigMap {Name}", desired.Name());
            await _client.CreateAsync(desired, cancellationToken);
        }
        else
        {
            existing.Data = desired.Data;
            existing.Metadata.Labels = desired.Metadata.Labels;
            _logger.LogDebug("Updating ConfigMap {Name}", desired.Name());
            await _client.UpdateAsync(existing, cancellationToken);
        }
    }

    private async Task ReconcileService(MinecraftServer server, CancellationToken cancellationToken)
    {
        var desired = ServiceBuilder.Build(server);
        var existing = await _client.GetAsync<V1Service>(desired.Name(), server.Namespace(), cancellationToken);

        if (existing is null)
        {
            _logger.LogInformation("Creating Service {Name}", desired.Name());
            await _client.CreateAsync(desired, cancellationToken);
        }
        else
        {
            // Update mutable fields; preserve ClusterIP and NodePort assignments
            existing.Spec.Type = desired.Spec.Type;
            existing.Spec.Ports = desired.Spec.Ports;
            existing.Spec.Selector = desired.Spec.Selector;
            existing.Metadata.Labels = desired.Metadata.Labels;
            existing.Metadata.Annotations = desired.Metadata.Annotations;
            _logger.LogDebug("Updating Service {Name}", desired.Name());
            await _client.UpdateAsync(existing, cancellationToken);
        }
    }

    private async Task<V1StatefulSet> ReconcileStatefulSet(MinecraftServer server, CancellationToken cancellationToken)
    {
        var desired = StatefulSetBuilder.Build(server);
        var existing = await _client.GetAsync<V1StatefulSet>(desired.Name(), server.Namespace(), cancellationToken);

        if (existing is null)
        {
            _logger.LogInformation("Creating StatefulSet {Name}", desired.Name());
            return await _client.CreateAsync(desired, cancellationToken);
        }

        // Update the pod template and replicas; StatefulSet handles rolling update
        existing.Spec.Template = desired.Spec.Template;
        existing.Spec.Replicas = desired.Spec.Replicas;
        existing.Metadata.Labels = desired.Metadata.Labels;
        _logger.LogDebug("Updating StatefulSet {Name}", desired.Name());
        return await _client.UpdateAsync(existing, cancellationToken);
    }

    private async Task UpdateStatus(MinecraftServer server, V1StatefulSet sts, CancellationToken cancellationToken)
    {
        // Determine phase based on replica counts
        var readyReplicas = sts.Status?.ReadyReplicas ?? 0;
        var desiredReplicas = server.Spec.Replicas;
        var image = StatefulSetBuilder.ResolveImage(server.Spec);

        MinecraftServerPhase phase;
        string message;

        if (desiredReplicas == 0)
        {
            phase = MinecraftServerPhase.Paused;
            message = "Server is paused (replicas=0)";
        }
        else if (readyReplicas >= desiredReplicas)
        {
            phase = MinecraftServerPhase.Running;
            message = "Server is running";
        }
        else
        {
            phase = MinecraftServerPhase.Provisioning;
            message = $"Waiting for pods: {readyReplicas}/{desiredReplicas} ready";
        }

        // Get service to update endpoint info
        var service = await _client.GetAsync<V1Service>(server.Name(), server.Namespace(), cancellationToken);
        var endpointStatus = BuildEndpointStatus(server, service);
        var pvcStatus = await BuildPvcStatus(server, cancellationToken);

        server.Status.Phase = phase;
        server.Status.Message = message;
        server.Status.ReadyReplicas = readyReplicas;
        server.Status.CurrentImage = image;
        server.Status.CurrentVersion = server.Spec.Server.Version;
        server.Status.ObservedGeneration = server.Generation() ?? 0;
        server.Status.Endpoint = endpointStatus;
        server.Status.Storage = pvcStatus;
        server.Status.Conditions = BuildConditions(phase, message, server.Generation() ?? 0);

        await _client.UpdateStatusAsync(server, cancellationToken);

        _logger.LogInformation(
            "MinecraftServer {Namespace}/{Name} phase={Phase} ready={Ready}/{Desired}",
            server.Namespace(), server.Name(), phase, readyReplicas, desiredReplicas);
    }

    private static EndpointStatus? BuildEndpointStatus(MinecraftServer server, V1Service? service)
    {
        if (service is null)
        {
            return null;
        }

        var port = server.Spec.Properties.ServerPort;
        string? host;

        if (server.Spec.Service.Type == ServiceType.LoadBalancer)
        {
            var ingress = service.Status?.LoadBalancer?.Ingress?.FirstOrDefault();
            host = ingress?.Ip ?? ingress?.Hostname ?? service.Spec?.ClusterIP;
        }
        else if (server.Spec.Service.Type == ServiceType.NodePort)
        {
            var nodePort = service.Spec?.Ports?.FirstOrDefault()?.NodePort;
            if (nodePort.HasValue)
            {
                port = nodePort.Value;
            }

            host = "<node-ip>";
        }
        else
        {
            host = service.Spec?.ClusterIP;
        }

        return new EndpointStatus { Host = host, Port = port };
    }

    private async Task<PvcStatus?> BuildPvcStatus(MinecraftServer server, CancellationToken cancellationToken)
    {
        if (!server.Spec.Storage.Enabled)
        {
            return null;
        }

        // StatefulSet PVC name follows the pattern: <volume-name>-<pod-name>
        var pvcName = $"data-{server.Name()}-0";
        var pvc = await _client.GetAsync<V1PersistentVolumeClaim>(pvcName, server.Namespace(), cancellationToken);

        if (pvc is null)
        {
            return null;
        }

        var capacity = pvc.Status?.Capacity?.TryGetValue("storage", out var cap) == true
            ? cap?.ToString()
            : null;

        return new PvcStatus
        {
            Name = pvcName,
            Phase = pvc.Status?.Phase,
            StorageClassName = pvc.Spec?.StorageClassName,
            Capacity = capacity,
        };
    }

    private async Task UpdatePhase(
        MinecraftServer server,
        MinecraftServerPhase phase,
        string message,
        CancellationToken cancellationToken)
    {
        server.Status.Phase = phase;
        server.Status.Message = message;
        try
        {
            await _client.UpdateStatusAsync(server, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update status for MinecraftServer {Namespace}/{Name}", server.Namespace(),
                server.Name());
        }
    }

    private static List<V1Condition> BuildConditions(
        MinecraftServerPhase phase,
        string message,
        long observedGeneration)
    {
        var now = DateTime.UtcNow;
        return
        [
            new V1Condition
            {
                Type = MinecraftServerConditions.Available,
                Status = phase == MinecraftServerPhase.Running ? "True" : "False",
                ObservedGeneration = observedGeneration,
                LastTransitionTime = now,
                Reason = phase == MinecraftServerPhase.Running ? "ServerRunning" : "ServerNotReady",
                Message = message,
            },
            // Progressing condition
            new V1Condition
            {
                Type = MinecraftServerConditions.Progressing,
                Status = phase == MinecraftServerPhase.Provisioning ? "True" : "False",
                ObservedGeneration = observedGeneration,
                LastTransitionTime = now,
                Reason = phase == MinecraftServerPhase.Provisioning ? "Reconciling" : "Idle",
                Message = message,
            },
            // Degraded condition
            new V1Condition
            {
                Type = MinecraftServerConditions.Degraded,
                Status = phase == MinecraftServerPhase.Failed ? "True" : "False",
                ObservedGeneration = observedGeneration,
                LastTransitionTime = now,
                Reason = phase == MinecraftServerPhase.Failed ? "ReconcileError" : "OK",
                Message = phase == MinecraftServerPhase.Failed ? message : "No issues",
            }
        ];
    }

    /// <summary>
    /// Ensures the desired container image is pre-pulled on the node running the
    /// current server pod before the StatefulSet rolling update is applied.
    /// </summary>
    /// <returns>
    /// <c>true</c> when no pre-pull is needed or the pre-pull has finished
    /// (caller may proceed with the StatefulSet update).
    /// <c>false</c> when a pre-pull Job has been created or is still running
    /// (caller should requeue and check again later).
    /// </returns>
    private async Task<bool> EnsureImagePrePulled(
        MinecraftServer server,
        string desiredImage,
        CancellationToken cancellationToken)
    {
        // Pre-pull is only relevant when the image is actually changing on an
        // existing (non-paused) server deployment.
        if (string.IsNullOrEmpty(server.Status.CurrentImage) ||
            server.Status.CurrentImage == desiredImage ||
            server.Spec.Replicas == 0)
        {
            return true;
        }

        var jobName = PrePullJobBuilder.JobName(server);
        var ns = server.Namespace();

        var existingJob = await _client.GetAsync<V1Job>(jobName, ns, cancellationToken);

        if (existingJob is not null)
        {
            // If the user changed the image again while a pre-pull was already
            // running, discard the stale Job and start fresh.
            var containers = existingJob.Spec?.Template?.Spec?.Containers;
            var jobImage = containers?.Count > 0 ? containers[0].Image : null;
            if (jobImage != desiredImage)
            {
                _logger.LogInformation(
                    "Image changed during pre-pull, recreating Job {Name} for image {Image}",
                    jobName, desiredImage);
                await _client.DeleteAsync<V1Job>(existingJob, cancellationToken);
                existingJob = null;
            }
        }

        if (existingJob is null)
        {
            var targetNode = await GetCurrentPodNode(server, cancellationToken);
            var job = PrePullJobBuilder.Build(server, desiredImage, targetNode);

            _logger.LogInformation(
                "Creating pre-pull Job {Name} for image {Image} on node {Node}",
                jobName, desiredImage, targetNode ?? "<any>");

            await _client.CreateAsync(job, cancellationToken);
            return false;
        }

        var conditions = existingJob.Status?.Conditions ?? [];

        if (conditions.Any(c => c.Type == "Complete" && c.Status == "True"))
        {
            _logger.LogInformation(
                "Pre-pull Job {Name} completed successfully, proceeding with upgrade", jobName);
            await _client.DeleteAsync<V1Job>(existingJob, cancellationToken);
            return true;
        }

        if (conditions.Any(c => c.Type == "Failed" && c.Status == "True"))
        {
            _logger.LogWarning(
                "Pre-pull Job {Name} failed (image pull may not be cached); proceeding with upgrade anyway",
                jobName);
            await _client.DeleteAsync<V1Job>(existingJob, cancellationToken);
            return true;
        }

        _logger.LogInformation("Waiting for pre-pull Job {Name} to complete…", jobName);
        return false;
    }

    /// <summary>
    /// Returns the Kubernetes node name of the StatefulSet pod (pod ordinal 0)
    /// for the given server, or <c>null</c> if the pod cannot be found.
    /// </summary>
    private async Task<string?> GetCurrentPodNode(MinecraftServer server, CancellationToken cancellationToken)
    {
        try
        {
            // StatefulSet pods are named <statefulset-name>-<ordinal>.
            var podName = $"{server.Name()}-0";
            var pod = await _client.GetAsync<V1Pod>(podName, server.Namespace(), cancellationToken);
            return pod?.Spec?.NodeName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not determine node for MinecraftServer {Namespace}/{Name}; pre-pull will target any node",
                server.Namespace(), server.Name());
            return null;
        }
    }
}
