using McOperator.Builders;
using McOperator.Entities;
using TUnit.Assertions.Extensions;

namespace McOperator.Tests;

/// <summary>
/// Tests for ServiceBuilder resource building logic.
/// </summary>
public class ServiceBuilderTests
{
    private static MinecraftServer BuildServer(Action<MinecraftServerSpec>? configure = null)
    {
        var server = new MinecraftServer();
        server.Metadata.Name = "my-server";
        server.Metadata.NamespaceProperty = "default";
        server.Metadata.Uid = "uid-123";
        server.Spec = new MinecraftServerSpec
        {
            AcceptEula = true,
            Server = new ServerSpec { Type = ServerType.Vanilla, Version = "LATEST" },
            Service = new ServiceSpec { Type = ServiceType.ClusterIP },
            Properties = new ServerPropertiesSpec { ServerPort = 25565 },
        };
        configure?.Invoke(server.Spec);
        return server;
    }

    [Test]
    public async Task Build_Service_HasCorrectName()
    {
        var server = BuildServer();
        var svc = ServiceBuilder.Build(server);

        await Assert.That(svc.Metadata.Name).IsEqualTo("my-server");
    }

    [Test]
    public async Task Build_Service_HasOwnerReference()
    {
        var server = BuildServer();
        var svc = ServiceBuilder.Build(server);

        await Assert.That(svc.Metadata.OwnerReferences.Count).IsEqualTo(1);
        await Assert.That(svc.Metadata.OwnerReferences[0].Kind).IsEqualTo("MinecraftServer");
    }

    [Test]
    public async Task Build_Service_DefaultsToClusterIP()
    {
        var server = BuildServer();
        var svc = ServiceBuilder.Build(server);

        await Assert.That(svc.Spec.Type).IsEqualTo("ClusterIP");
    }

    [Test]
    public async Task Build_Service_CanBeNodePort()
    {
        var server = BuildServer(s =>
        {
            s.Service.Type = ServiceType.NodePort;
            s.Service.NodePort = 30565;
        });
        var svc = ServiceBuilder.Build(server);

        await Assert.That(svc.Spec.Type).IsEqualTo("NodePort");
        await Assert.That(svc.Spec.Ports[0].NodePort).IsEqualTo(30565);
    }

    [Test]
    public async Task Build_Service_CanBeLoadBalancer()
    {
        var server = BuildServer(s => s.Service.Type = ServiceType.LoadBalancer);
        var svc = ServiceBuilder.Build(server);

        await Assert.That(svc.Spec.Type).IsEqualTo("LoadBalancer");
    }

    [Test]
    public async Task Build_Service_HasMinecraftPort()
    {
        var server = BuildServer(s => s.Properties.ServerPort = 25565);
        var svc = ServiceBuilder.Build(server);

        await Assert.That(svc.Spec.Ports.Count).IsEqualTo(1);
        await Assert.That(svc.Spec.Ports[0].Port).IsEqualTo(25565);
        await Assert.That(svc.Spec.Ports[0].Name).IsEqualTo("minecraft");
    }

    [Test]
    public async Task Build_Service_HasSelectorLabels()
    {
        var server = BuildServer();
        var svc = ServiceBuilder.Build(server);

        await Assert.That(svc.Spec.Selector.ContainsKey("app.kubernetes.io/instance")).IsTrue();
        await Assert.That(svc.Spec.Selector["app.kubernetes.io/instance"]).IsEqualTo("my-server");
    }

    [Test]
    public async Task Build_Service_HasAnnotations_WhenSpecified()
    {
        var server = BuildServer(s =>
        {
            s.Service.Annotations["service.beta.kubernetes.io/aws-load-balancer-type"] = "nlb";
        });
        var svc = ServiceBuilder.Build(server);

        await Assert.That(svc.Metadata.Annotations.ContainsKey("service.beta.kubernetes.io/aws-load-balancer-type"))
            .IsTrue();
    }

    [Test]
    public async Task Build_Service_NodePort_IsNull_WhenClusterIP()
    {
        var server = BuildServer(s =>
        {
            s.Service.Type = ServiceType.ClusterIP;
            s.Service.NodePort = null;
        });
        var svc = ServiceBuilder.Build(server);

        await Assert.That(svc.Spec.Ports[0].NodePort).IsNull();
    }
}
