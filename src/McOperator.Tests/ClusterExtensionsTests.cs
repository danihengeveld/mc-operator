using McOperator.Entities;
using McOperator.Extensions;

namespace McOperator.Tests;

/// <summary>
/// Tests for MinecraftServerClusterExtensions.
/// </summary>
public class ClusterExtensionsTests
{
    private static MinecraftServerCluster BuildCluster()
    {
        var cluster = new MinecraftServerCluster();
        cluster.Metadata.Name = "my-cluster";
        cluster.Metadata.NamespaceProperty = "default";
        cluster.Metadata.Uid = "uid-123";
        return cluster;
    }

    [Test]
    public async Task ServerName_ReturnsCorrectNameForIndex()
    {
        var cluster = BuildCluster();

        await Assert.That(cluster.ServerName(0)).IsEqualTo("my-cluster-0");
        await Assert.That(cluster.ServerName(1)).IsEqualTo("my-cluster-1");
        await Assert.That(cluster.ServerName(5)).IsEqualTo("my-cluster-5");
    }

    [Test]
    public async Task ProxyName_ReturnsCorrectName()
    {
        var cluster = BuildCluster();

        await Assert.That(cluster.ProxyName()).IsEqualTo("my-cluster-proxy");
    }

    [Test]
    public async Task ProxyConfigMapName_ReturnsCorrectName()
    {
        var cluster = BuildCluster();

        await Assert.That(cluster.ProxyConfigMapName()).IsEqualTo("my-cluster-proxy-config");
    }

    [Test]
    public async Task ProxyServiceName_ReturnsCorrectName()
    {
        var cluster = BuildCluster();

        await Assert.That(cluster.ProxyServiceName()).IsEqualTo("my-cluster-proxy");
    }

    [Test]
    public async Task MakeOwnerReference_HasCorrectKind()
    {
        var cluster = BuildCluster();
        var ownerRef = cluster.MakeOwnerReference();

        await Assert.That(ownerRef.Kind).IsEqualTo("MinecraftServerCluster");
        await Assert.That(ownerRef.ApiVersion).IsEqualTo("mc-operator.dhv.sh/v1alpha1");
        await Assert.That(ownerRef.Name).IsEqualTo("my-cluster");
        await Assert.That(ownerRef.Uid).IsEqualTo("uid-123");
        await Assert.That(ownerRef.Controller).IsTrue();
        await Assert.That(ownerRef.BlockOwnerDeletion).IsTrue();
    }

    [Test]
    public async Task ToMinecraftServerSpec_CopiesFields()
    {
        var template = new MinecraftServerTemplate
        {
            AcceptEula = true,
            Server = new ServerSpec { Type = ServerType.Paper, Version = "1.20.4" },
            Properties = new ServerPropertiesSpec { MaxPlayers = 30 },
            Jvm = new JvmSpec { MaxMemory = "2G" },
            Image = "custom-image:1.0",
            ImagePullPolicy = ImagePullPolicy.Always,
        };

        var spec = template.ToMinecraftServerSpec();

        await Assert.That(spec.AcceptEula).IsTrue();
        await Assert.That(spec.Server.Type).IsEqualTo(ServerType.Paper);
        await Assert.That(spec.Server.Version).IsEqualTo("1.20.4");
        await Assert.That(spec.Properties.MaxPlayers).IsEqualTo(30);
        await Assert.That(spec.Jvm.MaxMemory).IsEqualTo("2G");
        await Assert.That(spec.Image).IsEqualTo("custom-image:1.0");
        await Assert.That(spec.ImagePullPolicy).IsEqualTo(ImagePullPolicy.Always);
        await Assert.That(spec.Replicas).IsEqualTo(1);
    }

    [Test]
    public async Task ToMinecraftServerSpec_SetsReplicasToOne()
    {
        var template = new MinecraftServerTemplate { AcceptEula = true };
        var spec = template.ToMinecraftServerSpec();

        await Assert.That(spec.Replicas).IsEqualTo(1);
    }
}
