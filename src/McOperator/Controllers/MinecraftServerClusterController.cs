using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using McOperator.Builders;
using McOperator.Entities;
using McOperator.Extensions;

namespace McOperator.Controllers;

/// <summary>
/// Reconciles MinecraftServerCluster resources.
/// Manages the lifecycle of:
/// - Backend MinecraftServer instances (created/deleted to match desired count)
/// - Velocity proxy Deployment, Service, and ConfigMap
/// </summary>
[EntityRbac(typeof(MinecraftServerCluster), Verbs = RbacVerb.All)]
[EntityRbac(typeof(MinecraftServer), Verbs = RbacVerb.All)]
[GenericRbac(Groups = ["apps"], Resources = ["deployments"], Verbs = RbacVerb.All)]
[GenericRbac(Groups = [""], Resources = ["services", "configmaps"], Verbs = RbacVerb.All)]
[GenericRbac(Groups = [""], Resources = ["events"], Verbs = RbacVerb.Create | RbacVerb.Patch)]
[GenericRbac(Groups = ["coordination.k8s.io"], Resources = ["leases"], Verbs = RbacVerb.All)]
public class MinecraftServerClusterController : IEntityController<MinecraftServerCluster>
{
    private readonly IKubernetesClient _client;
    private readonly ILogger<MinecraftServerClusterController> _logger;

    public MinecraftServerClusterController(
        IKubernetesClient client,
        ILogger<MinecraftServerClusterController> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Called when a MinecraftServerCluster is created or updated.
    /// Reconciles all child resources to match the desired state.
    /// </summary>
    public async Task<ReconciliationResult<MinecraftServerCluster>> ReconcileAsync(
        MinecraftServerCluster cluster,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Reconciling MinecraftServerCluster {Namespace}/{Name} (generation {Generation})",
            cluster.Namespace(), cluster.Name(), cluster.Generation());

        await UpdatePhase(cluster, MinecraftServerClusterPhase.Provisioning, "Reconciling resources",
            cancellationToken);

        try
        {
            // 1. Determine desired server count
            var desiredCount = GetDesiredServerCount(cluster);

            if (cluster.Spec.Scaling.Mode == ScalingMode.Dynamic)
            {
                _logger.LogWarning(
                    "MinecraftServerCluster {Namespace}/{Name}: Dynamic scaling is configured but auto-scaling " +
                    "is not yet fully implemented. Running with minReplicas={MinReplicas}.",
                    cluster.Namespace(), cluster.Name(), cluster.Spec.Scaling.MinReplicas);
            }

            // 2. Reconcile backend MinecraftServer instances
            var existingServers = await ReconcileBackendServers(cluster, desiredCount, cancellationToken);

            // 3. Build server address list from existing servers
            var serverAddresses = BuildServerAddresses(cluster, existingServers);

            // 4. Reconcile Velocity proxy ConfigMap (must happen before Deployment)
            await ReconcileProxyConfigMap(cluster, serverAddresses, cancellationToken);

            // 5. Reconcile Velocity proxy Service
            await ReconcileProxyService(cluster, cancellationToken);

            // 6. Reconcile Velocity proxy Deployment
            var deployment = await ReconcileProxyDeployment(cluster, serverAddresses, cancellationToken);

            // 7. Update cluster status
            await UpdateStatus(cluster, existingServers, deployment, cancellationToken);

            return ReconciliationResult<MinecraftServerCluster>.Success(cluster,
                requeueAfter: TimeSpan.FromMinutes(5));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile MinecraftServerCluster {Namespace}/{Name}",
                cluster.Namespace(), cluster.Name());
            await UpdatePhase(cluster, MinecraftServerClusterPhase.Failed, ex.Message, cancellationToken);
            return ReconciliationResult<MinecraftServerCluster>.Failure(cluster, ex.Message, ex,
                TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    /// Called when a MinecraftServerCluster is deleted.
    /// Child resources owned via OwnerReference are garbage-collected automatically.
    /// </summary>
    public Task<ReconciliationResult<MinecraftServerCluster>> DeletedAsync(
        MinecraftServerCluster cluster,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "MinecraftServerCluster {Namespace}/{Name} deleted",
            cluster.Namespace(), cluster.Name());

        return Task.FromResult(ReconciliationResult<MinecraftServerCluster>.Success(cluster));
    }

    private static int GetDesiredServerCount(MinecraftServerCluster cluster)
    {
        var scaling = cluster.Spec.Scaling;

        if (scaling.Mode == ScalingMode.Static)
        {
            return scaling.Replicas;
        }

        // Dynamic scaling: start with minReplicas.
        // Full auto-scaling (reading player count metrics, computing desired replicas)
        // is not yet implemented. The cluster will run at minReplicas until auto-scaling
        // support is added in a future release.
        return scaling.MinReplicas;
    }

    private async Task<IList<MinecraftServer>> ReconcileBackendServers(
        MinecraftServerCluster cluster,
        int desiredCount,
        CancellationToken cancellationToken)
    {
        var ns = cluster.Namespace();
        var clusterName = cluster.Name();

        // List existing MinecraftServer instances owned by this cluster
        var allServers = await _client.ListAsync<MinecraftServer>(ns, cancellationToken: cancellationToken);
        var ownedServers = allServers
            .Where(s => s.Metadata.OwnerReferences?.Any(o =>
                o.Kind == "MinecraftServerCluster" && o.Name == clusterName) == true)
            .OrderBy(s => s.Name())
            .ToList();

        var currentCount = ownedServers.Count;

        // Scale up: create new servers
        if (currentCount < desiredCount)
        {
            for (var i = currentCount; i < desiredCount; i++)
            {
                var serverName = cluster.ServerName(i);
                var existing = ownedServers.FirstOrDefault(s => s.Name() == serverName);
                if (existing is null)
                {
                    await CreateBackendServer(cluster, serverName, cancellationToken);
                }
            }
        }
        // Scale down: delete excess servers (remove highest indices first)
        else if (currentCount > desiredCount)
        {
            var toRemove = ownedServers
                .Skip(desiredCount)
                .ToList();

            foreach (var server in toRemove)
            {
                _logger.LogInformation("Deleting backend server {Name} in {Namespace}",
                    server.Name(), ns);
                await _client.DeleteAsync(server, cancellationToken: cancellationToken);
            }
        }

        // Update existing servers if template changed
        foreach (var server in ownedServers.Take(desiredCount))
        {
            await UpdateBackendServer(cluster, server, cancellationToken);
        }

        // Return the current state (re-list after modifications)
        var updatedServers = await _client.ListAsync<MinecraftServer>(ns, cancellationToken: cancellationToken);
        return updatedServers
            .Where(s => s.Metadata.OwnerReferences?.Any(o =>
                o.Kind == "MinecraftServerCluster" && o.Name == clusterName) == true)
            .OrderBy(s => s.Name())
            .ToList();
    }

    private async Task CreateBackendServer(
        MinecraftServerCluster cluster,
        string serverName,
        CancellationToken cancellationToken)
    {
        var ns = cluster.Namespace();
        var spec = cluster.Spec.Template.ToMinecraftServerSpec();

        // When using Velocity modern forwarding, backend servers must have online-mode=false
        if (cluster.Spec.Proxy.PlayerForwardingMode == PlayerForwardingMode.Modern ||
            cluster.Spec.Proxy.PlayerForwardingMode == PlayerForwardingMode.BungeeGuard)
        {
            spec.Properties.OnlineMode = false;
        }

        // Backend servers use ClusterIP (internal-only); the proxy handles external traffic
        spec.Service = new ServiceSpec { Type = ServiceType.ClusterIP };

        var server = new MinecraftServer
        {
            Metadata = new V1ObjectMeta
            {
                Name = serverName,
                NamespaceProperty = ns,
                Labels = BuildBackendServerLabels(cluster, serverName),
                OwnerReferences = new List<V1OwnerReference>
                {
                    cluster.MakeOwnerReference(),
                },
            },
            Spec = spec,
        };

        _logger.LogInformation("Creating backend server {Name} in {Namespace}", serverName, ns);
        await _client.CreateAsync(server, cancellationToken);
    }

    private async Task UpdateBackendServer(
        MinecraftServerCluster cluster,
        MinecraftServer server,
        CancellationToken cancellationToken)
    {
        var desiredSpec = cluster.Spec.Template.ToMinecraftServerSpec();

        // Preserve online-mode override for proxied servers
        if (cluster.Spec.Proxy.PlayerForwardingMode == PlayerForwardingMode.Modern ||
            cluster.Spec.Proxy.PlayerForwardingMode == PlayerForwardingMode.BungeeGuard)
        {
            desiredSpec.Properties.OnlineMode = false;
        }

        // Backend servers always use ClusterIP
        desiredSpec.Service = new ServiceSpec { Type = ServiceType.ClusterIP };

        // Update the server spec to match the template.
        // We always apply the desired spec rather than diffing individual fields,
        // to prevent configuration drift when any template field changes.
        server.Spec = desiredSpec;
        server.Metadata.Labels = BuildBackendServerLabels(cluster, server.Name());

        _logger.LogDebug("Updating backend server {Name} in {Namespace}", server.Name(), server.Namespace());
        await _client.UpdateAsync(server, cancellationToken);
    }

    private static IList<ServerAddress> BuildServerAddresses(
        MinecraftServerCluster cluster,
        IList<MinecraftServer> servers)
    {
        return servers.Select((s, i) => new ServerAddress
        {
            Name = $"server-{i}",
            Address = s.Name(),
            Port = s.Spec.Properties.ServerPort,
        }).ToList();
    }

    private async Task ReconcileProxyConfigMap(
        MinecraftServerCluster cluster,
        IList<ServerAddress> serverAddresses,
        CancellationToken cancellationToken)
    {
        var configMapName = cluster.ProxyConfigMapName();
        var existing = await _client.GetAsync<V1ConfigMap>(configMapName, cluster.Namespace(), cancellationToken);

        // Preserve or generate forwarding secret
        var forwardingSecret = existing?.Data?.TryGetValue("forwarding.secret", out var secret) == true
            ? secret
            : VelocityProxyBuilder.GenerateForwardingSecret();

        var desired = VelocityProxyBuilder.BuildConfigMap(cluster, serverAddresses, forwardingSecret);

        if (existing is null)
        {
            _logger.LogInformation("Creating proxy ConfigMap {Name}", configMapName);
            await _client.CreateAsync(desired, cancellationToken);
        }
        else
        {
            existing.Data = desired.Data;
            existing.Metadata.Labels = desired.Metadata.Labels;
            _logger.LogDebug("Updating proxy ConfigMap {Name}", configMapName);
            await _client.UpdateAsync(existing, cancellationToken);
        }
    }

    private async Task ReconcileProxyService(
        MinecraftServerCluster cluster,
        CancellationToken cancellationToken)
    {
        var desired = VelocityProxyBuilder.BuildService(cluster);
        var existing =
            await _client.GetAsync<V1Service>(desired.Name(), cluster.Namespace(), cancellationToken);

        if (existing is null)
        {
            _logger.LogInformation("Creating proxy Service {Name}", desired.Name());
            await _client.CreateAsync(desired, cancellationToken);
        }
        else
        {
            existing.Spec.Type = desired.Spec.Type;
            existing.Spec.Ports = desired.Spec.Ports;
            existing.Spec.Selector = desired.Spec.Selector;
            existing.Metadata.Labels = desired.Metadata.Labels;
            existing.Metadata.Annotations = desired.Metadata.Annotations;
            _logger.LogDebug("Updating proxy Service {Name}", desired.Name());
            await _client.UpdateAsync(existing, cancellationToken);
        }
    }

    private async Task<V1Deployment> ReconcileProxyDeployment(
        MinecraftServerCluster cluster,
        IList<ServerAddress> serverAddresses,
        CancellationToken cancellationToken)
    {
        var desired = VelocityProxyBuilder.BuildDeployment(cluster, serverAddresses);
        var existing = await _client.GetAsync<V1Deployment>(desired.Name(), cluster.Namespace(), cancellationToken);

        if (existing is null)
        {
            _logger.LogInformation("Creating proxy Deployment {Name}", desired.Name());
            return await _client.CreateAsync(desired, cancellationToken);
        }

        existing.Spec.Template = desired.Spec.Template;
        existing.Spec.Replicas = desired.Spec.Replicas;
        existing.Metadata.Labels = desired.Metadata.Labels;
        _logger.LogDebug("Updating proxy Deployment {Name}", desired.Name());
        return await _client.UpdateAsync(existing, cancellationToken);
    }

    private async Task UpdateStatus(
        MinecraftServerCluster cluster,
        IList<MinecraftServer> servers,
        V1Deployment proxyDeployment,
        CancellationToken cancellationToken)
    {
        var readyServers = servers.Count(s => s.Status.Phase == MinecraftServerPhase.Running);
        var totalServers = servers.Count;
        var readyProxyReplicas = proxyDeployment.Status?.ReadyReplicas ?? 0;

        MinecraftServerClusterPhase phase;
        string message;

        if (readyServers == totalServers && totalServers > 0 && readyProxyReplicas > 0)
        {
            phase = MinecraftServerClusterPhase.Running;
            message = "Cluster is running";
        }
        else if (readyServers > 0 || readyProxyReplicas > 0)
        {
            phase = MinecraftServerClusterPhase.Degraded;
            message = $"Partial availability: {readyServers}/{totalServers} servers ready, " +
                      $"proxy {readyProxyReplicas}/1 ready";
        }
        else
        {
            phase = MinecraftServerClusterPhase.Provisioning;
            message = $"Starting: {readyServers}/{totalServers} servers ready";
        }

        // Build proxy endpoint info
        var proxyService = await _client.GetAsync<V1Service>(
            cluster.ProxyServiceName(), cluster.Namespace(), cancellationToken);
        var proxyEndpoint = BuildProxyEndpointStatus(cluster, proxyService);

        // Build server statuses
        var serverStatuses = servers.Select(s => new ClusterServerStatus
        {
            Name = s.Name(),
            Phase = s.Status.Phase,
        }).ToList();

        cluster.Status.Phase = phase;
        cluster.Status.Message = message;
        cluster.Status.ReadyServers = readyServers;
        cluster.Status.TotalServers = totalServers;
        cluster.Status.ReadyProxyReplicas = readyProxyReplicas;
        cluster.Status.ProxyEndpoint = proxyEndpoint;
        cluster.Status.Servers = serverStatuses;
        cluster.Status.ObservedGeneration = cluster.Generation() ?? 0;
        cluster.Status.Conditions = BuildConditions(phase, message, cluster.Generation() ?? 0);

        await _client.UpdateStatusAsync(cluster, cancellationToken);

        _logger.LogInformation(
            "MinecraftServerCluster {Namespace}/{Name} phase={Phase} servers={Ready}/{Total} proxy={ProxyReady}/1",
            cluster.Namespace(), cluster.Name(), phase, readyServers, totalServers, readyProxyReplicas);
    }

    private static EndpointStatus? BuildProxyEndpointStatus(
        MinecraftServerCluster cluster,
        V1Service? service)
    {
        if (service is null)
        {
            return null;
        }

        var port = cluster.Spec.Proxy.ProxyPort;
        string? host = null;

        if (cluster.Spec.Proxy.Service.Type == ServiceType.LoadBalancer)
        {
            var ingress = service.Status?.LoadBalancer?.Ingress?.FirstOrDefault();
            host = ingress?.Ip ?? ingress?.Hostname ?? service.Spec?.ClusterIP;
        }
        else if (cluster.Spec.Proxy.Service.Type == ServiceType.NodePort)
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

    private async Task UpdatePhase(
        MinecraftServerCluster cluster,
        MinecraftServerClusterPhase phase,
        string message,
        CancellationToken cancellationToken)
    {
        cluster.Status.Phase = phase;
        cluster.Status.Message = message;
        try
        {
            await _client.UpdateStatusAsync(cluster, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update status for MinecraftServerCluster {Namespace}/{Name}",
                cluster.Namespace(), cluster.Name());
        }
    }

    private static IList<V1Condition> BuildConditions(
        MinecraftServerClusterPhase phase,
        string message,
        long observedGeneration)
    {
        var conditions = new List<V1Condition>
        {
            new()
            {
                Type = MinecraftServerClusterConditions.Available,
                Status = phase == MinecraftServerClusterPhase.Running ? "True" : "False",
                ObservedGeneration = observedGeneration,
                LastTransitionTime = DateTime.UtcNow,
                Reason = phase == MinecraftServerClusterPhase.Running ? "ClusterRunning" : "ClusterNotReady",
                Message = message,
            },
            new()
            {
                Type = MinecraftServerClusterConditions.Progressing,
                Status = phase == MinecraftServerClusterPhase.Provisioning ? "True" : "False",
                ObservedGeneration = observedGeneration,
                LastTransitionTime = DateTime.UtcNow,
                Reason = phase == MinecraftServerClusterPhase.Provisioning ? "Reconciling" : "Idle",
                Message = message,
            },
            new()
            {
                Type = MinecraftServerClusterConditions.Degraded,
                Status = phase is MinecraftServerClusterPhase.Failed or MinecraftServerClusterPhase.Degraded
                    ? "True"
                    : "False",
                ObservedGeneration = observedGeneration,
                LastTransitionTime = DateTime.UtcNow,
                Reason = phase == MinecraftServerClusterPhase.Failed
                    ? "ReconcileError"
                    : phase == MinecraftServerClusterPhase.Degraded
                        ? "PartialAvailability"
                        : "OK",
                Message = phase is MinecraftServerClusterPhase.Failed or MinecraftServerClusterPhase.Degraded
                    ? message
                    : "No issues",
            },
        };

        return conditions;
    }

    private static IDictionary<string, string> BuildBackendServerLabels(
        MinecraftServerCluster cluster,
        string serverName) =>
        new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = "minecraft-server",
            ["app.kubernetes.io/instance"] = serverName,
            ["app.kubernetes.io/managed-by"] = "mc-operator",
            ["app.kubernetes.io/component"] = "backend",
            ["mc-operator.dhv.sh/cluster-name"] = cluster.Name(),
            ["mc-operator.dhv.sh/server-name"] = serverName,
        };
}
