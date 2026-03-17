using FluentAssertions;
using McOperator.Builders;
using McOperator.Entities;
using Xunit;

namespace McOperator.Tests;

/// <summary>
/// Tests for StatefulSetBuilder resource building logic.
/// </summary>
public class StatefulSetBuilderTests
{
    private static MinecraftServer BuildServer(Action<MinecraftServerSpec>? configure = null)
    {
        var server = new MinecraftServer();
        server.Metadata.Name = "test-server";
        server.Metadata.NamespaceProperty = "default";
        server.Metadata.Uid = "test-uid-1234";
        server.Spec = new MinecraftServerSpec
        {
            AcceptEula = true,
            Server = new ServerSpec { Type = ServerType.Paper, Version = "1.20.4" },
            Jvm = new JvmSpec { InitialMemory = "512m", MaxMemory = "2G" },
            Resources = new ResourcesSpec
            {
                CpuRequest = "500m",
                MemoryRequest = "2Gi",
                MemoryLimit = "3Gi",
            },
            Storage = new StorageSpec { Enabled = true, Size = "10Gi", MountPath = "/data" },
            Properties = new ServerPropertiesSpec { ServerPort = 25565 },
        };
        configure?.Invoke(server.Spec);
        return server;
    }

    [Fact]
    public void Build_ReturnsStatefulSet_WithCorrectName()
    {
        var server = BuildServer();
        var sts = StatefulSetBuilder.Build(server);

        sts.Metadata.Name.Should().Be("test-server");
        sts.Metadata.NamespaceProperty.Should().Be("default");
    }

    [Fact]
    public void Build_StatefulSet_HasOwnerReference()
    {
        var server = BuildServer();
        var sts = StatefulSetBuilder.Build(server);

        sts.Metadata.OwnerReferences.Should().HaveCount(1);
        sts.Metadata.OwnerReferences[0].Kind.Should().Be("MinecraftServer");
        sts.Metadata.OwnerReferences[0].Name.Should().Be("test-server");
        sts.Metadata.OwnerReferences[0].Uid.Should().Be("test-uid-1234");
        sts.Metadata.OwnerReferences[0].Controller.Should().BeTrue();
    }

    [Fact]
    public void Build_StatefulSet_HasCorrectReplicas()
    {
        var server = BuildServer(s => s.Replicas = 1);
        var sts = StatefulSetBuilder.Build(server);

        sts.Spec.Replicas.Should().Be(1);
    }

    [Fact]
    public void Build_StatefulSet_HasZeroReplicas_WhenPaused()
    {
        var server = BuildServer(s => s.Replicas = 0);
        var sts = StatefulSetBuilder.Build(server);

        sts.Spec.Replicas.Should().Be(0);
    }

    [Fact]
    public void Build_Container_HasCorrectImage_ForPaper()
    {
        var server = BuildServer(s => s.Server.Type = ServerType.Paper);
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        container.Image.Should().Be("itzg/minecraft-server:latest");
    }

    [Fact]
    public void Build_Container_UsesCustomImage_WhenSpecified()
    {
        var server = BuildServer(s => s.Image = "my-custom-image:1.0");
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        container.Image.Should().Be("my-custom-image:1.0");
    }

    [Fact]
    public void Build_Container_HasEulaEnvVar()
    {
        var server = BuildServer(s => s.AcceptEula = true);
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var eulaEnv = container.Env.FirstOrDefault(e => e.Name == "EULA");
        eulaEnv.Should().NotBeNull();
        eulaEnv!.Value.Should().Be("TRUE");
    }

    [Fact]
    public void Build_Container_HasServerTypeEnvVar()
    {
        var server = BuildServer(s => s.Server.Type = ServerType.Spigot);
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var typeEnv = container.Env.FirstOrDefault(e => e.Name == "TYPE");
        typeEnv.Should().NotBeNull();
        typeEnv!.Value.Should().Be("SPIGOT");
    }

    [Fact]
    public void Build_Container_HasVersionEnvVar()
    {
        var server = BuildServer(s => s.Server.Version = "1.20.4");
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var versionEnv = container.Env.FirstOrDefault(e => e.Name == "VERSION");
        versionEnv.Should().NotBeNull();
        versionEnv!.Value.Should().Be("1.20.4");
    }

    [Fact]
    public void Build_Container_HasJvmOptsWithMemory()
    {
        var server = BuildServer(s =>
        {
            s.Jvm.InitialMemory = "512m";
            s.Jvm.MaxMemory = "2G";
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var jvmEnv = container.Env.FirstOrDefault(e => e.Name == "JVM_OPTS");
        jvmEnv.Should().NotBeNull();
        jvmEnv!.Value.Should().Contain("-Xms512m");
        jvmEnv.Value.Should().Contain("-Xmx2G");
    }

    [Fact]
    public void Build_Container_HasExtraJvmArgs()
    {
        var server = BuildServer(s =>
        {
            s.Jvm.ExtraJvmArgs.Add("-XX:+UseG1GC");
            s.Jvm.ExtraJvmArgs.Add("-XX:G1HeapRegionSize=4M");
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var jvmEnv = container.Env.FirstOrDefault(e => e.Name == "JVM_OPTS");
        jvmEnv!.Value.Should().Contain("-XX:+UseG1GC");
        jvmEnv.Value.Should().Contain("-XX:G1HeapRegionSize=4M");
    }

    [Fact]
    public void Build_Container_HasResourceRequests()
    {
        var server = BuildServer(s =>
        {
            s.Resources.CpuRequest = "500m";
            s.Resources.MemoryRequest = "2Gi";
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        container.Resources.Requests.Should().ContainKey("cpu");
        container.Resources.Requests.Should().ContainKey("memory");
        container.Resources.Requests["cpu"].ToString().Should().Be("500m");
        container.Resources.Requests["memory"].ToString().Should().Be("2Gi");
    }

    [Fact]
    public void Build_Container_MemoryLimit_DefaultsToRequest()
    {
        var server = BuildServer(s =>
        {
            s.Resources.MemoryRequest = "2Gi";
            s.Resources.MemoryLimit = null;
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        container.Resources.Limits.Should().ContainKey("memory");
        container.Resources.Limits["memory"].ToString().Should().Be("2Gi");
    }

    [Fact]
    public void Build_HasVolumeClaimTemplate_WhenStorageEnabled()
    {
        var server = BuildServer(s =>
        {
            s.Storage.Enabled = true;
            s.Storage.Size = "10Gi";
        });
        var sts = StatefulSetBuilder.Build(server);

        sts.Spec.VolumeClaimTemplates.Should().NotBeNullOrEmpty();
        var pvc = sts.Spec.VolumeClaimTemplates![0];
        pvc.Metadata.Name.Should().Be("data");
        pvc.Spec.Resources.Requests["storage"].ToString().Should().Be("10Gi");
    }

    [Fact]
    public void Build_NoVolumeClaimTemplate_WhenStorageDisabled()
    {
        var server = BuildServer(s => s.Storage.Enabled = false);
        var sts = StatefulSetBuilder.Build(server);

        sts.Spec.VolumeClaimTemplates.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Build_Container_HasDataVolumeMount_WhenStorageEnabled()
    {
        var server = BuildServer(s =>
        {
            s.Storage.Enabled = true;
            s.Storage.MountPath = "/data";
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var mount = container.VolumeMounts.FirstOrDefault(m => m.Name == "data");
        mount.Should().NotBeNull();
        mount!.MountPath.Should().Be("/data");
    }

    [Fact]
    public void Build_StorageRetentionPolicy_DefaultsToRetain()
    {
        var server = BuildServer(s => s.Storage.DeleteWithServer = false);
        var sts = StatefulSetBuilder.Build(server);

        sts.Spec.PersistentVolumeClaimRetentionPolicy!.WhenDeleted.Should().Be("Retain");
    }

    [Fact]
    public void Build_StorageRetentionPolicy_IsDelete_WhenDeleteWithServerTrue()
    {
        var server = BuildServer(s => s.Storage.DeleteWithServer = true);
        var sts = StatefulSetBuilder.Build(server);

        sts.Spec.PersistentVolumeClaimRetentionPolicy!.WhenDeleted.Should().Be("Delete");
    }

    [Fact]
    public void Build_Container_HasReadinessAndLivenessProbes()
    {
        var server = BuildServer();
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        container.ReadinessProbe.Should().NotBeNull();
        container.LivenessProbe.Should().NotBeNull();
    }

    [Fact]
    public void Build_HasStandardLabels()
    {
        var server = BuildServer();
        var sts = StatefulSetBuilder.Build(server);

        sts.Metadata.Labels.Should().ContainKey("app.kubernetes.io/name");
        sts.Metadata.Labels.Should().ContainKey("app.kubernetes.io/instance");
        sts.Metadata.Labels["app.kubernetes.io/instance"].Should().Be("test-server");
    }

    [Fact]
    public void Build_Container_HasOpsEnvVar_WhenOpsSpecified()
    {
        var server = BuildServer(s =>
        {
            s.Properties.Ops.Add("admin");
            s.Properties.Ops.Add("moderator");
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var opsEnv = container.Env.FirstOrDefault(e => e.Name == "OPS");
        opsEnv.Should().NotBeNull();
        opsEnv!.Value.Should().Be("admin,moderator");
    }

    [Fact]
    public void Build_Container_HasWhitelistEnvVar_WhenWhitelistSpecified()
    {
        var server = BuildServer(s =>
        {
            s.Properties.WhitelistEnabled = true;
            s.Properties.Whitelist.Add("player1");
            s.Properties.Whitelist.Add("player2");
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var whitelistEnv = container.Env.FirstOrDefault(e => e.Name == "WHITELIST");
        whitelistEnv.Should().NotBeNull();
        whitelistEnv!.Value.Should().Be("player1,player2");
    }

    [Fact]
    public void Build_Container_HasMotdEnvVar_WhenMotdSpecified()
    {
        var server = BuildServer(s => s.Properties.Motd = "Welcome to my server!");
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var motdEnv = container.Env.FirstOrDefault(e => e.Name == "MOTD");
        motdEnv.Should().NotBeNull();
        motdEnv!.Value.Should().Be("Welcome to my server!");
    }

    [Fact]
    public void ResolveImage_ReturnsDefaultImage_WhenNoImageSpecified()
    {
        var spec = new MinecraftServerSpec { Server = new ServerSpec { Type = ServerType.Vanilla } };

        var image = StatefulSetBuilder.ResolveImage(spec);

        image.Should().Be("itzg/minecraft-server:latest");
    }

    [Fact]
    public void ResolveImage_ReturnsCustomImage_WhenImageSpecified()
    {
        var spec = new MinecraftServerSpec
        {
            Server = new ServerSpec { Type = ServerType.Paper },
            Image = "my-registry/mc-server:custom",
        };

        var image = StatefulSetBuilder.ResolveImage(spec);

        image.Should().Be("my-registry/mc-server:custom");
    }
}
