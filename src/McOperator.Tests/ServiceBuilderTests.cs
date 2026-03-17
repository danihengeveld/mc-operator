using FluentAssertions;
using McOperator.Builders;
using McOperator.Entities;
using Xunit;

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

    [Fact]
    public void Build_Service_HasCorrectName()
    {
        var server = BuildServer();
        var svc = ServiceBuilder.Build(server);

        svc.Metadata.Name.Should().Be("my-server");
    }

    [Fact]
    public void Build_Service_HasOwnerReference()
    {
        var server = BuildServer();
        var svc = ServiceBuilder.Build(server);

        svc.Metadata.OwnerReferences.Should().HaveCount(1);
        svc.Metadata.OwnerReferences[0].Kind.Should().Be("MinecraftServer");
    }

    [Fact]
    public void Build_Service_DefaultsToClusterIP()
    {
        var server = BuildServer();
        var svc = ServiceBuilder.Build(server);

        svc.Spec.Type.Should().Be("ClusterIP");
    }

    [Fact]
    public void Build_Service_CanBeNodePort()
    {
        var server = BuildServer(s =>
        {
            s.Service.Type = ServiceType.NodePort;
            s.Service.NodePort = 30565;
        });
        var svc = ServiceBuilder.Build(server);

        svc.Spec.Type.Should().Be("NodePort");
        svc.Spec.Ports[0].NodePort.Should().Be(30565);
    }

    [Fact]
    public void Build_Service_CanBeLoadBalancer()
    {
        var server = BuildServer(s => s.Service.Type = ServiceType.LoadBalancer);
        var svc = ServiceBuilder.Build(server);

        svc.Spec.Type.Should().Be("LoadBalancer");
    }

    [Fact]
    public void Build_Service_HasMinecraftPort()
    {
        var server = BuildServer(s => s.Properties.ServerPort = 25565);
        var svc = ServiceBuilder.Build(server);

        svc.Spec.Ports.Should().HaveCount(1);
        svc.Spec.Ports[0].Port.Should().Be(25565);
        svc.Spec.Ports[0].Name.Should().Be("minecraft");
    }

    [Fact]
    public void Build_Service_HasSelectorLabels()
    {
        var server = BuildServer();
        var svc = ServiceBuilder.Build(server);

        svc.Spec.Selector.Should().ContainKey("app.kubernetes.io/instance");
        svc.Spec.Selector["app.kubernetes.io/instance"].Should().Be("my-server");
    }

    [Fact]
    public void Build_Service_HasAnnotations_WhenSpecified()
    {
        var server = BuildServer(s =>
        {
            s.Service.Annotations["service.beta.kubernetes.io/aws-load-balancer-type"] = "nlb";
        });
        var svc = ServiceBuilder.Build(server);

        svc.Metadata.Annotations.Should().ContainKey("service.beta.kubernetes.io/aws-load-balancer-type");
    }

    [Fact]
    public void Build_Service_NodePort_IsNull_WhenClusterIP()
    {
        var server = BuildServer(s =>
        {
            s.Service.Type = ServiceType.ClusterIP;
            s.Service.NodePort = null;
        });
        var svc = ServiceBuilder.Build(server);

        svc.Spec.Ports[0].NodePort.Should().BeNull();
    }
}
