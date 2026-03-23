using McOperator.Builders;
using McOperator.Entities;

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
            Resources = new ResourcesSpec { CpuRequest = "500m", MemoryRequest = "2Gi", MemoryLimit = "3Gi", },
            Storage = new StorageSpec { Enabled = true, Size = "10Gi", MountPath = "/data" },
            Properties = new ServerPropertiesSpec { ServerPort = 25565 },
        };
        configure?.Invoke(server.Spec);
        return server;
    }

    [Test]
    public async Task Build_ReturnsStatefulSet_WithCorrectName()
    {
        var server = BuildServer();
        var sts = StatefulSetBuilder.Build(server);

        await Assert.That(sts.Metadata.Name).IsEqualTo("test-server");
        await Assert.That(sts.Metadata.NamespaceProperty).IsEqualTo("default");
    }

    [Test]
    public async Task Build_StatefulSet_HasOwnerReference()
    {
        var server = BuildServer();
        var sts = StatefulSetBuilder.Build(server);

        await Assert.That(sts.Metadata.OwnerReferences.Count).IsEqualTo(1);
        await Assert.That(sts.Metadata.OwnerReferences[0].Kind).IsEqualTo("MinecraftServer");
        await Assert.That(sts.Metadata.OwnerReferences[0].Name).IsEqualTo("test-server");
        await Assert.That(sts.Metadata.OwnerReferences[0].Uid).IsEqualTo("test-uid-1234");
        await Assert.That(sts.Metadata.OwnerReferences[0].Controller).IsTrue();
    }

    [Test]
    public async Task Build_StatefulSet_HasCorrectReplicas()
    {
        var server = BuildServer(s => s.Replicas = 1);
        var sts = StatefulSetBuilder.Build(server);

        await Assert.That(sts.Spec.Replicas).IsEqualTo(1);
    }

    [Test]
    public async Task Build_StatefulSet_HasZeroReplicas_WhenPaused()
    {
        var server = BuildServer(s => s.Replicas = 0);
        var sts = StatefulSetBuilder.Build(server);

        await Assert.That(sts.Spec.Replicas).IsEqualTo(0);
    }

    [Test]
    public async Task Build_Container_HasCorrectImage_ForPaper()
    {
        var server = BuildServer(s => s.Server.Type = ServerType.Paper);
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Image).IsEqualTo("itzg/minecraft-server:latest");
    }

    [Test]
    public async Task Build_Container_UsesCustomImage_WhenSpecified()
    {
        var server = BuildServer(s => s.Image = "my-custom-image:1.0");
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Image).IsEqualTo("my-custom-image:1.0");
    }

    [Test]
    public async Task Build_Container_HasEulaEnvVar()
    {
        var server = BuildServer(s => s.AcceptEula = true);
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var eulaEnv = container.Env.FirstOrDefault(e => e.Name == "EULA");
        await Assert.That(eulaEnv).IsNotNull();
        await Assert.That(eulaEnv!.Value).IsEqualTo("TRUE");
    }

    [Test]
    public async Task Build_Container_HasServerTypeEnvVar()
    {
        var server = BuildServer(s => s.Server.Type = ServerType.Spigot);
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var typeEnv = container.Env.FirstOrDefault(e => e.Name == "TYPE");
        await Assert.That(typeEnv).IsNotNull();
        await Assert.That(typeEnv!.Value).IsEqualTo("SPIGOT");
    }

    [Test]
    public async Task Build_Container_HasVersionEnvVar()
    {
        var server = BuildServer(s => s.Server.Version = "1.20.4");
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var versionEnv = container.Env.FirstOrDefault(e => e.Name == "VERSION");
        await Assert.That(versionEnv).IsNotNull();
        await Assert.That(versionEnv!.Value).IsEqualTo("1.20.4");
    }

    [Test]
    public async Task Build_Container_HasJvmOptsWithMemory()
    {
        var server = BuildServer(s =>
        {
            s.Jvm.InitialMemory = "512m";
            s.Jvm.MaxMemory = "2G";
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var jvmEnv = container.Env.FirstOrDefault(e => e.Name == "JVM_OPTS");
        await Assert.That(jvmEnv).IsNotNull();
        await Assert.That(jvmEnv!.Value).Contains("-Xms512m");
        await Assert.That(jvmEnv.Value).Contains("-Xmx2G");
    }

    [Test]
    public async Task Build_Container_HasExtraJvmArgs()
    {
        var server = BuildServer(s =>
        {
            s.Jvm.ExtraJvmArgs.Add("-XX:+UseG1GC");
            s.Jvm.ExtraJvmArgs.Add("-XX:G1HeapRegionSize=4M");
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var jvmEnv = container.Env.FirstOrDefault(e => e.Name == "JVM_OPTS");
        await Assert.That(jvmEnv!.Value).Contains("-XX:+UseG1GC");
        await Assert.That(jvmEnv.Value).Contains("-XX:G1HeapRegionSize=4M");
    }

    [Test]
    public async Task Build_Container_HasResourceRequests()
    {
        var server = BuildServer(s =>
        {
            s.Resources.CpuRequest = "500m";
            s.Resources.MemoryRequest = "2Gi";
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Resources.Requests.ContainsKey("cpu")).IsTrue();
        await Assert.That(container.Resources.Requests.ContainsKey("memory")).IsTrue();
        await Assert.That(container.Resources.Requests["cpu"].ToString()).IsEqualTo("500m");
        await Assert.That(container.Resources.Requests["memory"].ToString()).IsEqualTo("2Gi");
    }

    [Test]
    public async Task Build_Container_MemoryLimit_DefaultsToRequest()
    {
        var server = BuildServer(s =>
        {
            s.Resources.MemoryRequest = "2Gi";
            s.Resources.MemoryLimit = null;
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Resources.Limits.ContainsKey("memory")).IsTrue();
        await Assert.That(container.Resources.Limits["memory"].ToString()).IsEqualTo("2Gi");
    }

    [Test]
    public async Task Build_HasVolumeClaimTemplate_WhenStorageEnabled()
    {
        var server = BuildServer(s =>
        {
            s.Storage.Enabled = true;
            s.Storage.Size = "10Gi";
        });
        var sts = StatefulSetBuilder.Build(server);

        await Assert.That(sts.Spec.VolumeClaimTemplates).IsNotNull();
        await Assert.That(sts.Spec.VolumeClaimTemplates!.Count).IsGreaterThan(0);
        var pvc = sts.Spec.VolumeClaimTemplates![0];
        await Assert.That(pvc.Metadata.Name).IsEqualTo("data");
        await Assert.That(pvc.Spec.Resources.Requests["storage"].ToString()).IsEqualTo("10Gi");
    }

    [Test]
    public async Task Build_NoVolumeClaimTemplate_WhenStorageDisabled()
    {
        var server = BuildServer(s => s.Storage.Enabled = false);
        var sts = StatefulSetBuilder.Build(server);

        bool isNullOrEmpty = sts.Spec.VolumeClaimTemplates is null || sts.Spec.VolumeClaimTemplates.Count == 0;
        await Assert.That(isNullOrEmpty).IsTrue();
    }

    [Test]
    public async Task Build_Container_HasDataVolumeMount_WhenStorageEnabled()
    {
        var server = BuildServer(s =>
        {
            s.Storage.Enabled = true;
            s.Storage.MountPath = "/data";
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var mount = container.VolumeMounts.FirstOrDefault(m => m.Name == "data");
        await Assert.That(mount).IsNotNull();
        await Assert.That(mount!.MountPath).IsEqualTo("/data");
    }

    [Test]
    public async Task Build_StorageRetentionPolicy_DefaultsToRetain()
    {
        var server = BuildServer(s => s.Storage.DeleteWithServer = false);
        var sts = StatefulSetBuilder.Build(server);

        await Assert.That(sts.Spec.PersistentVolumeClaimRetentionPolicy!.WhenDeleted).IsEqualTo("Retain");
    }

    [Test]
    public async Task Build_StorageRetentionPolicy_IsDelete_WhenDeleteWithServerTrue()
    {
        var server = BuildServer(s => s.Storage.DeleteWithServer = true);
        var sts = StatefulSetBuilder.Build(server);

        await Assert.That(sts.Spec.PersistentVolumeClaimRetentionPolicy!.WhenDeleted).IsEqualTo("Delete");
    }

    [Test]
    public async Task Build_Container_HasReadinessAndLivenessProbes()
    {
        var server = BuildServer();
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        await Assert.That(container.ReadinessProbe).IsNotNull();
        await Assert.That(container.LivenessProbe).IsNotNull();
    }

    [Test]
    public async Task Build_HasStandardLabels()
    {
        var server = BuildServer();
        var sts = StatefulSetBuilder.Build(server);

        await Assert.That(sts.Metadata.Labels.ContainsKey("app.kubernetes.io/name")).IsTrue();
        await Assert.That(sts.Metadata.Labels.ContainsKey("app.kubernetes.io/instance")).IsTrue();
        await Assert.That(sts.Metadata.Labels["app.kubernetes.io/instance"]).IsEqualTo("test-server");
    }

    [Test]
    public async Task Build_Container_HasOpsEnvVar_WhenOpsSpecified()
    {
        var server = BuildServer(s =>
        {
            s.Properties.Ops.Add("admin");
            s.Properties.Ops.Add("moderator");
        });
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var opsEnv = container.Env.FirstOrDefault(e => e.Name == "OPS");
        await Assert.That(opsEnv).IsNotNull();
        await Assert.That(opsEnv!.Value).IsEqualTo("admin,moderator");
    }

    [Test]
    public async Task Build_Container_HasWhitelistEnvVar_WhenWhitelistSpecified()
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
        await Assert.That(whitelistEnv).IsNotNull();
        await Assert.That(whitelistEnv!.Value).IsEqualTo("player1,player2");
    }

    [Test]
    public async Task Build_Container_HasMotdEnvVar_WhenMotdSpecified()
    {
        var server = BuildServer(s => s.Properties.Motd = "Welcome to my server!");
        var sts = StatefulSetBuilder.Build(server);

        var container = sts.Spec.Template.Spec.Containers[0];
        var motdEnv = container.Env.FirstOrDefault(e => e.Name == "MOTD");
        await Assert.That(motdEnv).IsNotNull();
        await Assert.That(motdEnv!.Value).IsEqualTo("Welcome to my server!");
    }

    [Test]
    public async Task ResolveImage_ReturnsDefaultImage_WhenNoImageSpecified()
    {
        var spec = new MinecraftServerSpec { Server = new ServerSpec { Type = ServerType.Vanilla } };

        var image = StatefulSetBuilder.ResolveImage(spec);

        await Assert.That(image).IsEqualTo("itzg/minecraft-server:latest");
    }

    [Test]
    public async Task ResolveImage_ReturnsCustomImage_WhenImageSpecified()
    {
        var spec = new MinecraftServerSpec
        {
            Server = new ServerSpec { Type = ServerType.Paper }, Image = "my-registry/mc-server:custom",
        };

        var image = StatefulSetBuilder.ResolveImage(spec);

        await Assert.That(image).IsEqualTo("my-registry/mc-server:custom");
    }
}
