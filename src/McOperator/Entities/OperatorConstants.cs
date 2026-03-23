namespace McOperator.Entities;

/// <summary>
/// Shared constants for the mc-operator: CRD metadata, label keys/values,
/// default images, and finalizer identifiers.
/// </summary>
public static class OperatorConstants
{
    public const string OperatorName = "mc-operator";

    // CRD identity
    // WARNING: Changing Group or Version is a breaking change that requires CRD migration.
    // All existing custom resources in the cluster reference these values.
    public const string Group = "mc-operator.dhv.sh";
    public const string Version = "v1alpha1";
    public const string ApiVersion = $"{Group}/{Version}";
    public const string ServerKind = "MinecraftServer";
    public const string ServerPluralName = "minecraftservers";
    public const string ClusterKind = "MinecraftServerCluster";
    public const string ClusterPluralName = "minecraftserverclusters";

    // Finalizer identifiers
    public const string ServerFinalizer = $"{Group}/finalizer";
    public const string ClusterFinalizer = $"{Group}/cluster-finalizer";

    // Operator-specific label keys
    public const string ServerNameLabel = $"{Group}/server-name";
    public const string ClusterNameLabel = $"{Group}/cluster-name";
    public const string ConfigLabel = $"{Group}/config";
    public const string PrePullLabel = $"{Group}/pre-pull";

    // Kubernetes well-known label keys
    public const string AppNameLabel = "app.kubernetes.io/name";
    public const string AppInstanceLabel = "app.kubernetes.io/instance";
    public const string AppManagedByLabel = "app.kubernetes.io/managed-by";
    public const string AppComponentLabel = "app.kubernetes.io/component";

    // Well-known label values
    public const string ServerAppName = "minecraft-server";
    public const string ProxyAppName = "velocity-proxy";
    public const string ComponentBackend = "backend";
    public const string ComponentProxy = "proxy";

    // Default container images
    public const string DefaultServerImage = "itzg/minecraft-server:latest";
    public const string DefaultProxyImage = "itzg/mc-proxy:latest";
}
