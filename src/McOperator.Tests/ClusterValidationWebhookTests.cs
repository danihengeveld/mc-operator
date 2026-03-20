using McOperator.Entities;
using McOperator.Webhooks;
using TUnit.Assertions.Extensions;

namespace McOperator.Tests;

/// <summary>
/// Tests for MinecraftServerClusterValidationWebhook validation logic.
/// </summary>
public class ClusterValidationWebhookTests
{
    private static MinecraftServerCluster ValidCluster(Action<MinecraftServerClusterSpec>? configure = null)
    {
        var cluster = new MinecraftServerCluster
        {
            Spec = new MinecraftServerClusterSpec
            {
                Template = new MinecraftServerTemplate
                {
                    AcceptEula = true,
                    Server = new ServerSpec { Type = ServerType.Paper, Version = "1.20.4" },
                    Jvm = new JvmSpec { InitialMemory = "512m", MaxMemory = "1G" },
                    Resources = new ResourcesSpec
                    {
                        CpuRequest = "500m",
                        MemoryRequest = "1Gi",
                    },
                    Storage = new StorageSpec
                    {
                        Enabled = true,
                        Size = "10Gi",
                        MountPath = "/data",
                    },
                    Properties = new ServerPropertiesSpec
                    {
                        ServerPort = 25565,
                        MaxPlayers = 20,
                        ViewDistance = 10,
                        SimulationDistance = 10,
                        LevelName = "world",
                    },
                },
                Scaling = new ScalingSpec
                {
                    Mode = ScalingMode.Static,
                    Replicas = 3,
                },
                Proxy = new VelocityProxySpec
                {
                    ProxyPort = 25577,
                    MaxPlayers = 100,
                    OnlineMode = true,
                    Service = new ServiceSpec { Type = ServiceType.ClusterIP },
                },
            },
        };

        configure?.Invoke(cluster.Spec);
        return cluster;
    }

    [Test]
    public async Task Create_ValidCluster_ReturnsSuccess()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster();

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsTrue();
    }

    [Test]
    public async Task Create_EulaNotAccepted_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s => s.Template.AcceptEula = false);

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("acceptEula");
    }

    [Test]
    public async Task Create_EmptyVersion_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s => s.Template.Server.Version = "");

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("version");
    }

    [Test]
    public async Task Create_JvmMaxLessThanInitial_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Template.Jvm.InitialMemory = "2G";
            s.Template.Jvm.MaxMemory = "1G";
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("maxMemory");
    }

    [Test]
    public async Task Create_InvalidStorageSize_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s => s.Template.Storage.Size = "not-a-size");

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("storage.size");
    }

    [Test]
    public async Task Create_RelativeMountPath_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s => s.Template.Storage.MountPath = "relative/path");

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("mountPath");
    }

    // ========== Scaling Validation ==========

    [Test]
    public async Task Create_StaticScaling_ValidReplicas_ReturnsSuccess()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Scaling.Mode = ScalingMode.Static;
            s.Scaling.Replicas = 5;
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsTrue();
    }

    [Test]
    public async Task Create_StaticScaling_ZeroReplicas_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Scaling.Mode = ScalingMode.Static;
            s.Scaling.Replicas = 0;
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("replicas");
    }

    [Test]
    public async Task Create_DynamicScaling_Valid_ReturnsSuccess()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Scaling.Mode = ScalingMode.Dynamic;
            s.Scaling.MinReplicas = 1;
            s.Scaling.MaxReplicas = 10;
            s.Scaling.Policy = new ScalingPolicy
            {
                Metric = ScalingMetric.PlayerCount,
                TargetPercentage = 80,
            };
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsTrue();
    }

    [Test]
    public async Task Create_DynamicScaling_MaxLessThanMin_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Scaling.Mode = ScalingMode.Dynamic;
            s.Scaling.MinReplicas = 5;
            s.Scaling.MaxReplicas = 2;
            s.Scaling.Policy = new ScalingPolicy();
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("maxReplicas");
    }

    [Test]
    public async Task Create_DynamicScaling_NoPolicySet_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Scaling.Mode = ScalingMode.Dynamic;
            s.Scaling.MinReplicas = 1;
            s.Scaling.MaxReplicas = 10;
            s.Scaling.Policy = null;
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("policy");
    }

    [Test]
    public async Task Create_DynamicScaling_InvalidTargetPercentage_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Scaling.Mode = ScalingMode.Dynamic;
            s.Scaling.MinReplicas = 1;
            s.Scaling.MaxReplicas = 10;
            s.Scaling.Policy = new ScalingPolicy { TargetPercentage = 0 };
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("targetPercentage");
    }

    // ========== Proxy Validation ==========

    [Test]
    public async Task Create_ProxyPortOutOfRange_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s => s.Proxy.ProxyPort = 80);

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("proxyPort");
    }

    [Test]
    public async Task Create_ProxyMaxPlayersZero_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s => s.Proxy.MaxPlayers = 0);

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("maxPlayers");
    }

    [Test]
    public async Task Create_ProxyNodePortOnClusterIP_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Proxy.Service.Type = ServiceType.ClusterIP;
            s.Proxy.Service.NodePort = 30577;
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("nodePort");
    }

    [Test]
    public async Task Create_ProxyNodePortOutOfRange_ReturnsFail()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Proxy.Service.Type = ServiceType.NodePort;
            s.Proxy.Service.NodePort = 1234;
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("30000-32767");
    }

    [Test]
    public async Task Create_MultipleErrors_AllReported()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var cluster = ValidCluster(s =>
        {
            s.Template.AcceptEula = false;
            s.Template.Server.Version = "";
            s.Proxy.ProxyPort = 80;
        });

        var result = webhook.Create(cluster, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("acceptEula");
        await Assert.That(result.Status.Message).Contains("version");
        await Assert.That(result.Status.Message).Contains("proxyPort");
    }

    [Test]
    public async Task Update_ValidCluster_ReturnsSuccess()
    {
        var webhook = new MinecraftServerClusterValidationWebhook();
        var oldCluster = ValidCluster();
        var newCluster = ValidCluster(s => s.Scaling.Replicas = 5);

        var result = webhook.Update(newCluster, oldCluster, dryRun: false);

        await Assert.That(result.Valid).IsTrue();
    }
}
