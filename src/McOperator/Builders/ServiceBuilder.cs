using k8s.Models;
using McOperator.Entities;
using McOperator.Extensions;

namespace McOperator.Builders;

/// <summary>
/// Builds Kubernetes Service manifests for MinecraftServer resources.
/// </summary>
public static class ServiceBuilder
{
    /// <summary>
    /// Builds a Service for the given MinecraftServer.
    /// </summary>
    public static V1Service Build(MinecraftServer server)
    {
        var name = server.Name();
        var ns = server.Namespace();
        var spec = server.Spec;
        var labels = BuildLabels(server);
        var selectorLabels = BuildSelectorLabels(server);

        var ports = new List<V1ServicePort>
        {
            new()
            {
                Name = "minecraft",
                Protocol = "TCP",
                Port = spec.Properties.ServerPort,
                TargetPort = spec.Properties.ServerPort,
                NodePort = spec.Service.Type == ServiceType.NodePort ? spec.Service.NodePort : null,
            },
        };

        var annotations = new Dictionary<string, string>(spec.Service.Annotations);

        return new V1Service
        {
            ApiVersion = "v1",
            Kind = "Service",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = ns,
                Labels = labels,
                Annotations = annotations.Count > 0 ? annotations : null,
                OwnerReferences = new List<V1OwnerReference>
                {
                    server.MakeOwnerReference(),
                },
            },
            Spec = new V1ServiceSpec
            {
                Type = spec.Service.Type.ToString(),
                Selector = selectorLabels,
                Ports = ports,
            },
        };
    }

    private static IDictionary<string, string> BuildLabels(MinecraftServer server) =>
        new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = "minecraft-server",
            ["app.kubernetes.io/instance"] = server.Name(),
            ["app.kubernetes.io/managed-by"] = "mc-operator",
            ["mc-operator.dhv.sh/server-name"] = server.Name(),
        };

    private static IDictionary<string, string> BuildSelectorLabels(MinecraftServer server) =>
        new Dictionary<string, string>
        {
            ["app.kubernetes.io/instance"] = server.Name(),
            ["mc-operator.dhv.sh/server-name"] = server.Name(),
        };
}
