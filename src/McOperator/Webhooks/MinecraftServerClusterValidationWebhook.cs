using KubeOps.Operator.Web.Webhooks.Admission.Validation;
using McOperator.Entities;

namespace McOperator.Webhooks;

/// <summary>
/// Validation webhook for MinecraftServerCluster resources.
/// Rejects invalid configurations before they reach the reconciler.
/// </summary>
[ValidationWebhook(typeof(MinecraftServerCluster))]
public class MinecraftServerClusterValidationWebhook : ValidationWebhook<MinecraftServerCluster>
{
    public override ValidationResult Create(MinecraftServerCluster cluster, bool dryRun)
    {
        return Validate(cluster);
    }

    public override ValidationResult Update(MinecraftServerCluster newCluster, MinecraftServerCluster oldCluster,
        bool dryRun)
    {
        return Validate(newCluster);
    }

    private ValidationResult Validate(MinecraftServerCluster cluster)
    {
        var errors = new List<string>();

        ValidateTemplate(cluster, errors);
        ValidateScaling(cluster, errors);
        ValidateProxy(cluster, errors);

        if (errors.Count > 0)
        {
            return Fail(string.Join("; ", errors), 422);
        }

        return Success();
    }

    /// <summary>
    /// Validates the backend server template.
    /// </summary>
    private static void ValidateTemplate(MinecraftServerCluster cluster, List<string> errors)
    {
        var template = cluster.Spec.Template;

        if (!template.AcceptEula)
        {
            errors.Add(
                "spec.template.acceptEula must be true. You must accept the Minecraft EULA " +
                "(https://account.mojang.com/documents/minecraft_eula) to run Minecraft servers.");
        }

        var version = template.Server.Version;
        if (string.IsNullOrWhiteSpace(version))
        {
            errors.Add(
                "spec.template.server.version must not be empty. Use 'LATEST' or a specific version like '1.20.4'.");
        }

        // JVM memory validation
        var jvm = template.Jvm;
        if (!MinecraftServerValidationWebhook.TryParseMemory(jvm.InitialMemory, out var initialMb))
        {
            errors.Add($"spec.template.jvm.initialMemory '{jvm.InitialMemory}' is not a valid memory value.");
        }

        if (!MinecraftServerValidationWebhook.TryParseMemory(jvm.MaxMemory, out var maxMb))
        {
            errors.Add($"spec.template.jvm.maxMemory '{jvm.MaxMemory}' is not a valid memory value.");
        }

        if (initialMb > 0 && maxMb > 0 && maxMb < initialMb)
        {
            errors.Add(
                $"spec.template.jvm.maxMemory ({jvm.MaxMemory}) must be >= spec.template.jvm.initialMemory ({jvm.InitialMemory}).");
        }

        // Storage validation
        var storage = template.Storage;
        if (storage.Enabled && !MinecraftServerValidationWebhook.IsValidK8sQuantity(storage.Size))
        {
            errors.Add($"spec.template.storage.size '{storage.Size}' is not a valid Kubernetes quantity.");
        }

        if (storage.Enabled && string.IsNullOrWhiteSpace(storage.MountPath))
        {
            errors.Add("spec.template.storage.mountPath must not be empty when storage is enabled.");
        }

        if (storage.Enabled && !storage.MountPath.StartsWith('/'))
        {
            errors.Add("spec.template.storage.mountPath must be an absolute path (start with '/').");
        }

        // Properties validation
        var props = template.Properties;
        if (props.ServerPort < 1024 || props.ServerPort > 65535)
        {
            errors.Add(
                $"spec.template.properties.serverPort ({props.ServerPort}) must be between 1024 and 65535.");
        }

        if (props.MaxPlayers < 1)
        {
            errors.Add("spec.template.properties.maxPlayers must be at least 1.");
        }

        if (props.ViewDistance < 3 || props.ViewDistance > 32)
        {
            errors.Add(
                $"spec.template.properties.viewDistance ({props.ViewDistance}) must be between 3 and 32.");
        }

        if (props.SimulationDistance < 3 || props.SimulationDistance > 32)
        {
            errors.Add(
                $"spec.template.properties.simulationDistance ({props.SimulationDistance}) must be between 3 and 32.");
        }
    }

    /// <summary>
    /// Validates scaling configuration.
    /// </summary>
    private static void ValidateScaling(MinecraftServerCluster cluster, List<string> errors)
    {
        var scaling = cluster.Spec.Scaling;

        if (scaling.Mode == ScalingMode.Static)
        {
            if (scaling.Replicas < 1)
            {
                errors.Add("spec.scaling.replicas must be at least 1 for static scaling mode.");
            }
        }
        else if (scaling.Mode == ScalingMode.Dynamic)
        {
            if (scaling.MinReplicas < 1)
            {
                errors.Add("spec.scaling.minReplicas must be at least 1 for dynamic scaling mode.");
            }

            if (scaling.MaxReplicas < scaling.MinReplicas)
            {
                errors.Add(
                    $"spec.scaling.maxReplicas ({scaling.MaxReplicas}) must be >= spec.scaling.minReplicas ({scaling.MinReplicas}).");
            }

            if (scaling.Policy is null)
            {
                errors.Add("spec.scaling.policy is required for dynamic scaling mode.");
            }
            else if (scaling.Policy.TargetPercentage < 1 || scaling.Policy.TargetPercentage > 100)
            {
                errors.Add(
                    $"spec.scaling.policy.targetPercentage ({scaling.Policy.TargetPercentage}) must be between 1 and 100.");
            }
        }
    }

    /// <summary>
    /// Validates Velocity proxy configuration.
    /// </summary>
    private static void ValidateProxy(MinecraftServerCluster cluster, List<string> errors)
    {
        var proxy = cluster.Spec.Proxy;

        if (proxy.ProxyPort < 1024 || proxy.ProxyPort > 65535)
        {
            errors.Add($"spec.proxy.proxyPort ({proxy.ProxyPort}) must be between 1024 and 65535.");
        }

        if (proxy.MaxPlayers < 1)
        {
            errors.Add("spec.proxy.maxPlayers must be at least 1.");
        }

        // Service validation
        var svc = proxy.Service;
        if (svc.NodePort.HasValue && svc.Type != ServiceType.NodePort)
        {
            errors.Add(
                "spec.proxy.service.nodePort is only valid when spec.proxy.service.type is 'NodePort'.");
        }

        if (svc.Type == ServiceType.NodePort && svc.NodePort.HasValue)
        {
            if (svc.NodePort.Value < 30000 || svc.NodePort.Value > 32767)
            {
                errors.Add($"spec.proxy.service.nodePort ({svc.NodePort}) must be in the range 30000-32767.");
            }
        }
    }
}
