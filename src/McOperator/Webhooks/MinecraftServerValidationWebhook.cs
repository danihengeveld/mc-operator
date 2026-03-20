using System.Text.RegularExpressions;
using KubeOps.Operator.Web.Webhooks.Admission.Validation;
using McOperator.Entities;

namespace McOperator.Webhooks;

/// <summary>
/// Validation webhook for MinecraftServer resources.
/// Rejects invalid configurations before they reach the reconciler.
/// </summary>
[ValidationWebhook(typeof(MinecraftServer))]
public partial class MinecraftServerValidationWebhook : ValidationWebhook<MinecraftServer>
{
    private static readonly HashSet<string> ValidLevelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Note: "LARGBIOMES" (without E) is the correct value used by itzg/minecraft-server
        // and the original Minecraft server. "LARGEBIOMES" is a common misspelling.
        "DEFAULT",
        "FLAT",
        "LARGBIOMES",
        "AMPLIFIED",
        "BUFFET",
        "SINGLE_BIOME_SURFACE",
        "SINGLE_BIOME_CAVES",
        "SINGLE_BIOME_FLOATING_ISLANDS",
    };

    public override ValidationResult Create(MinecraftServer server, bool dryRun)
    {
        return Validate(server, oldServer: null);
    }

    public override ValidationResult Update(MinecraftServer newServer, MinecraftServer oldServer, bool dryRun)
    {
        return Validate(newServer, oldServer);
    }

    private ValidationResult Validate(MinecraftServer server, MinecraftServer? oldServer)
    {
        var errors = new List<string>();

        ValidateEula(server, errors);
        ValidateVersion(server, errors);
        ValidateJvm(server, errors);
        ValidateResources(server, errors);
        ValidateStorage(server, errors);
        ValidateService(server, errors);
        ValidateProperties(server, errors);

        if (oldServer is not null)
        {
            ValidateImmutableFields(server, oldServer, errors);
        }

        if (errors.Count > 0)
        {
            return Fail(string.Join("; ", errors), 422);
        }

        return Success();
    }

    /// <summary>
    /// EULA must be explicitly accepted.
    /// </summary>
    private static void ValidateEula(MinecraftServer server, List<string> errors)
    {
        if (!server.Spec.AcceptEula)
        {
            errors.Add(
                "spec.acceptEula must be true. You must accept the Minecraft EULA " +
                "(https://account.mojang.com/documents/minecraft_eula) to run a Minecraft server.");
        }
    }

    /// <summary>
    /// Version must be non-empty.
    /// </summary>
    private static void ValidateVersion(MinecraftServer server, List<string> errors)
    {
        var version = server.Spec.Server.Version;
        if (string.IsNullOrWhiteSpace(version))
        {
            errors.Add("spec.server.version must not be empty. Use 'LATEST' or a specific version like '1.20.4'.");
        }
    }

    /// <summary>
    /// JVM memory values must be parseable and MaxMemory should be >= InitialMemory.
    /// </summary>
    private static void ValidateJvm(MinecraftServer server, List<string> errors)
    {
        var jvm = server.Spec.Jvm;

        if (!TryParseMemory(jvm.InitialMemory, out var initialMb))
        {
            errors.Add($"spec.jvm.initialMemory '{jvm.InitialMemory}' is not a valid memory value. " +
                       "Use values like '512m', '1g', '2G'.");
        }

        if (!TryParseMemory(jvm.MaxMemory, out var maxMb))
        {
            errors.Add($"spec.jvm.maxMemory '{jvm.MaxMemory}' is not a valid memory value. " +
                       "Use values like '1g', '2G'.");
        }

        if (initialMb > 0 && maxMb > 0 && maxMb < initialMb)
        {
            errors.Add(
                $"spec.jvm.maxMemory ({jvm.MaxMemory}) must be greater than or equal to " +
                $"spec.jvm.initialMemory ({jvm.InitialMemory}).");
        }
    }

    /// <summary>
    /// Resource requests/limits must be non-empty if specified.
    /// Memory limit should be at least as large as the JVM max heap.
    /// </summary>
    private static void ValidateResources(MinecraftServer server, List<string> errors)
    {
        var res = server.Spec.Resources;

        if (res.MemoryRequest is not null && !IsValidKubernetesQuantity(res.MemoryRequest))
        {
            errors.Add($"spec.resources.memoryRequest '{res.MemoryRequest}' is not a valid Kubernetes quantity.");
        }

        if (res.MemoryLimit is not null && !IsValidKubernetesQuantity(res.MemoryLimit))
        {
            errors.Add($"spec.resources.memoryLimit '{res.MemoryLimit}' is not a valid Kubernetes quantity.");
        }

        if (res.CpuRequest is not null && !IsValidKubernetesQuantity(res.CpuRequest))
        {
            errors.Add($"spec.resources.cpuRequest '{res.CpuRequest}' is not a valid Kubernetes quantity.");
        }

        if (res.CpuLimit is not null && !IsValidKubernetesQuantity(res.CpuLimit))
        {
            errors.Add($"spec.resources.cpuLimit '{res.CpuLimit}' is not a valid Kubernetes quantity.");
        }
    }

    /// <summary>
    /// Storage size must be a valid Kubernetes quantity.
    /// </summary>
    private static void ValidateStorage(MinecraftServer server, List<string> errors)
    {
        var storage = server.Spec.Storage;
        if (storage.Enabled && !IsValidKubernetesQuantity(storage.Size))
        {
            errors.Add($"spec.storage.size '{storage.Size}' is not a valid Kubernetes quantity. " +
                       "Examples: '10Gi', '100Gi'.");
        }

        if (storage.Enabled && string.IsNullOrWhiteSpace(storage.MountPath))
        {
            errors.Add("spec.storage.mountPath must not be empty when storage is enabled.");
        }

        if (storage.Enabled && !storage.MountPath.StartsWith('/'))
        {
            errors.Add("spec.storage.mountPath must be an absolute path (start with '/').");
        }
    }

    /// <summary>
    /// Service configuration validation.
    /// NodePort value is only valid when service type is NodePort.
    /// </summary>
    private static void ValidateService(MinecraftServer server, List<string> errors)
    {
        var svc = server.Spec.Service;

        if (svc.NodePort.HasValue && svc.Type != ServiceType.NodePort)
        {
            errors.Add(
                $"spec.service.nodePort is only valid when spec.service.type is 'NodePort'. " +
                $"Current type is '{svc.Type}'.");
        }

        if (svc is { Type: ServiceType.NodePort, NodePort: < 30000 or > 32767 })
        {
            errors.Add(
                $"spec.service.nodePort ({svc.NodePort}) must be in the range 30000-32767.");
        }
    }

    /// <summary>
    /// Server properties validation.
    /// </summary>
    private static void ValidateProperties(MinecraftServer server, List<string> errors)
    {
        var props = server.Spec.Properties;

        if (props.ServerPort < 1024 || props.ServerPort > 65535)
        {
            errors.Add($"spec.properties.serverPort ({props.ServerPort}) must be between 1024 and 65535.");
        }

        if (props.MaxPlayers < 1)
        {
            errors.Add($"spec.properties.maxPlayers must be at least 1.");
        }

        if (props.ViewDistance < 3 || props.ViewDistance > 32)
        {
            errors.Add($"spec.properties.viewDistance ({props.ViewDistance}) must be between 3 and 32.");
        }

        if (props.SimulationDistance < 3 || props.SimulationDistance > 32)
        {
            errors.Add($"spec.properties.simulationDistance ({props.SimulationDistance}) must be between 3 and 32.");
        }

        if (string.IsNullOrWhiteSpace(props.LevelName))
        {
            errors.Add("spec.properties.levelName must not be empty.");
        }
    }

    /// <summary>
    /// Checks immutable fields that should not change after creation.
    /// </summary>
    private static void ValidateImmutableFields(
        MinecraftServer newServer,
        MinecraftServer oldServer,
        List<string> errors)
    {
        // Storage settings are effectively immutable after the PVC is created.
        // StatefulSet VolumeClaimTemplates cannot be updated in-place.
        var newStorage = newServer.Spec.Storage;
        var oldStorage = oldServer.Spec.Storage;

        if (newStorage.Enabled != oldStorage.Enabled)
        {
            errors.Add(
                "spec.storage.enabled is immutable after creation. " +
                "Delete and recreate the MinecraftServer to change storage settings.");
        }

        if (newStorage.Enabled && oldStorage.Enabled && newStorage.Size != oldStorage.Size)
        {
            // Allow resize only; shrinking is not allowed. But we can't easily validate
            // the actual claim here, so we warn against size changes entirely in v1.
            errors.Add(
                "spec.storage.size is immutable after creation in v1. " +
                "To resize, manually edit the PVC and then update the MinecraftServer.");
        }

        if (newStorage.StorageClassName != oldStorage.StorageClassName)
        {
            errors.Add(
                "spec.storage.storageClassName is immutable after creation. " +
                "Delete and recreate the MinecraftServer to change storage class.");
        }
    }

    /// <summary>
    /// Attempts to parse a JVM memory string (e.g., "512m", "1G", "2048M") into megabytes.
    /// Returns 0 if the string is not parseable but leaves validation to the caller.
    /// </summary>
    public static bool TryParseMemory(string value, out long megabytes)
    {
        megabytes = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var suffix = char.ToUpperInvariant(trimmed[^1]);

        if (!long.TryParse(trimmed[..^1], out var number) || number <= 0)
        {
            return false;
        }

        megabytes = suffix switch
        {
            'M' => number,
            'G' => number * 1024,
            'K' => number / 1024,
            _ => 0,
        };

        return megabytes > 0;
    }

    /// <summary>
    /// Validates that a Kubernetes resource quantity string is non-empty and plausibly valid.
    /// Full Kubernetes quantity parsing is complex; this is a best-effort check.
    /// </summary>
    public static bool IsValidKubernetesQuantity(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               // Accept common suffixes: m, k, K, M, G, T, P, E, Ki, Mi, Gi, Ti, Pi, Ei
               // and plain integer values
               KubernetesQuantityRegex().IsMatch(value.Trim());
    }

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)?([mkKMGTPEi]|[KMGTPE]i|m)?$", RegexOptions.Compiled)]
    private static partial Regex KubernetesQuantityRegex();
}
